using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Documents;

public record DocumentDto(
    Guid Id, string Name, string OriginalFileName, string ContentType,
    long Size, string Kind, Guid UploadedByUserId, DateTime CreatedAt);

public record DocumentContentInfo(string StorageKey, string ContentType, string OriginalFileName, string Kind);

public record DocumentNoteDto(Guid Id, Guid DocumentId, Guid UserId, string Body, DateTime CreatedAt, DateTime? UpdatedAt);

/* ---------- helpers ---------- */

public static class DocumentKinds
{
    public static string FromFileName(string fileName)
    {
        var ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".doc" or ".docx" or ".rtf" or ".odt" => "word",
            ".xls" or ".xlsx" or ".csv" or ".ods" => "spreadsheet",
            _ => "other",
        };
    }

    public static bool IsAllowed(string fileName)
    {
        var ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
        return ext is ".doc" or ".docx" or ".rtf" or ".odt"
            or ".xls" or ".xlsx" or ".csv" or ".ods";
    }
}

/* ---------- create (metadata; file already saved → storageKey) ---------- */

public record CreateDocumentCommand(
    string Name, string OriginalFileName, string ContentType, long Size, string StorageKey)
    : IRequest<DocumentDto>;

public class CreateDocumentValidator : AbstractValidator<CreateDocumentCommand>
{
    public CreateDocumentValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.OriginalFileName).NotEmpty().MaximumLength(260);
        RuleFor(x => x.StorageKey).NotEmpty();
    }
}

/* ---------- queries / commands ---------- */

public record ListDocumentsQuery() : IRequest<IReadOnlyList<DocumentDto>>;
public record GetDocumentContentQuery(Guid Id) : IRequest<DocumentContentInfo>;
public record DeleteDocumentCommand(Guid Id) : IRequest<Unit>;

public record ListDocumentNotesQuery(Guid DocumentId) : IRequest<IReadOnlyList<DocumentNoteDto>>;
public record AddDocumentNoteCommand(Guid DocumentId, string Body) : IRequest<DocumentNoteDto>;

public class AddDocumentNoteValidator : AbstractValidator<AddDocumentNoteCommand>
{
    public AddDocumentNoteValidator()
    {
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.Body).NotEmpty().MaximumLength(4000);
    }
}

/* ---------- handlers ---------- */

public class DocumentsHandler :
    IRequestHandler<CreateDocumentCommand, DocumentDto>,
    IRequestHandler<ListDocumentsQuery, IReadOnlyList<DocumentDto>>,
    IRequestHandler<GetDocumentContentQuery, DocumentContentInfo>,
    IRequestHandler<DeleteDocumentCommand, Unit>,
    IRequestHandler<ListDocumentNotesQuery, IReadOnlyList<DocumentNoteDto>>,
    IRequestHandler<AddDocumentNoteCommand, DocumentNoteDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IFileStorage _files;

    public DocumentsHandler(IApplicationDbContext db, ICurrentUser user, IFileStorage files)
    {
        _db = Guard.AgainstNull(db); _user = Guard.AgainstNull(user); _files = Guard.AgainstNull(files);
    }

    // Upload/delete are manager-only; viewing is open to anyone in the agency.
    private void EnsureManager()
    {
        if (_user.AgencyId is null || _user.UserId is null) throw new ForbiddenAccessException();
        if (!_user.Roles.Contains("Admin") && !_user.Roles.Contains("ProgramManager")
            && !_user.Roles.Contains("SuperAdmin"))
            throw new ForbiddenAccessException("Only managers can manage documents.");
    }

    public async Task<DocumentDto> Handle(CreateDocumentCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureManager();
        if (!DocumentKinds.IsAllowed(request.OriginalFileName))
            throw new ConflictException("Only Word documents and spreadsheets are allowed.");

        var doc = new Document
        {
            AgencyId = _user.AgencyId!.Value,
            UploadedByUserId = _user.UserId!.Value,
            Name = request.Name.Trim(),
            OriginalFileName = request.OriginalFileName,
            ContentType = request.ContentType,
            Size = request.Size,
            StorageKey = request.StorageKey,
            Kind = DocumentKinds.FromFileName(request.OriginalFileName),
        };
        _db.Documents.Add(doc);
        await _db.SaveChangesAsync(ct);
        return Map(doc);
    }

    public async Task<IReadOnlyList<DocumentDto>> Handle(ListDocumentsQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) throw new ForbiddenAccessException();
        // TenantEntity global filter already scopes to the caller's agency/office.
        return await _db.Documents
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new DocumentDto(
                d.Id, d.Name, d.OriginalFileName, d.ContentType, d.Size, d.Kind, d.UploadedByUserId, d.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<DocumentContentInfo> Handle(GetDocumentContentQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) throw new ForbiddenAccessException();
        var d = await _db.Documents.FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Document), request.Id);
        return new DocumentContentInfo(d.StorageKey, d.ContentType, d.OriginalFileName, d.Kind);
    }

    public async Task<Unit> Handle(DeleteDocumentCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureManager();
        var d = await _db.Documents.FirstOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Document), request.Id);
        d.IsDeleted = true;
        await _db.SaveChangesAsync(ct);
        await _files.DeleteAsync(d.StorageKey, ct);
        return Unit.Value;
    }

    public async Task<IReadOnlyList<DocumentNoteDto>> Handle(ListDocumentNotesQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null) throw new ForbiddenAccessException();
        // Ensure the document exists & is visible to this agency before exposing notes.
        var exists = await _db.Documents.AnyAsync(x => x.Id == request.DocumentId, ct);
        if (!exists) throw new NotFoundException(nameof(Document), request.DocumentId);

        return await _db.DocumentNotes
            .Where(n => n.DocumentId == request.DocumentId)
            .OrderBy(n => n.CreatedAt)
            .Select(n => new DocumentNoteDto(n.Id, n.DocumentId, n.UserId, n.Body, n.CreatedAt, n.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<DocumentNoteDto> Handle(AddDocumentNoteCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        if (_user.UserId is null || _user.AgencyId is null) throw new ForbiddenAccessException();
        var doc = await _db.Documents.FirstOrDefaultAsync(x => x.Id == request.DocumentId, ct)
            ?? throw new NotFoundException(nameof(Document), request.DocumentId);

        var note = new DocumentNote
        {
            AgencyId = doc.AgencyId,
            DocumentId = doc.Id,
            UserId = _user.UserId.Value,
            Body = request.Body.Trim(),
        };
        _db.DocumentNotes.Add(note);
        await _db.SaveChangesAsync(ct);
        return new DocumentNoteDto(note.Id, note.DocumentId, note.UserId, note.Body, note.CreatedAt, note.UpdatedAt);
    }

    private static DocumentDto Map(Document d) => new(
        d.Id, d.Name, d.OriginalFileName, d.ContentType, d.Size, d.Kind, d.UploadedByUserId, d.CreatedAt);
}
