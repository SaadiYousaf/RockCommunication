namespace CRM.Infrastructure.Integrations;

public class IntegrationOptions
{
    public JornayaOptions Jornaya { get; set; } = new();
    public DialerOptions Dialer { get; set; } = new();
    public SmsOptions Sms { get; set; } = new();
    public EmailOptions Email { get; set; } = new();
    public CarrierOptions Carriers { get; set; } = new();
    public FundingOptions Funding { get; set; } = new();
}

public class JornayaOptions
{
    public string Provider { get; set; } = "Stub";
    public string? BaseUrl { get; set; }
    public string? AccountId { get; set; }
    public string? Token { get; set; }
    public int TimeoutSeconds { get; set; } = 10;
}

public class DialerOptions
{
    /// <summary>Active telephony backend: "Vici" | "Zoom" | "RingCentral" (case-insensitive). "Stub" / empty BaseUrl falls back to the logging stub.</summary>
    public string Provider { get; set; } = "Vici";
    public string? BaseUrl { get; set; }

    // ---- Vici (ViciDial agc/api.php) ----
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Source { get; set; } = "CRM";

    // ---- Zoom Phone (server-to-server OAuth) / RingCentral (OAuth) ----
    /// <summary>Zoom account id (server-to-server OAuth) or RingCentral account id (defaults to "~").</summary>
    public string? AccountId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    /// <summary>Pre-issued bearer/JWT token, used when an OAuth exchange is not configured (RingCentral JWT, Zoom token).</summary>
    public string? ApiToken { get; set; }
    /// <summary>OAuth token endpoint (e.g. https://zoom.us/oauth/token or https://platform.ringcentral.com/restapi/oauth/token).</summary>
    public string? TokenUrl { get; set; }
    /// <summary>Caller id / extension the outbound call originates from.</summary>
    public string? FromNumber { get; set; }

    public int TimeoutSeconds { get; set; } = 10;
}

public class SmsOptions
{
    public string Provider { get; set; } = "Stub";
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? FromNumber { get; set; }
}

public class EmailOptions
{
    public string Provider { get; set; } = "Stub";
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@crm.local";
    public string FromName { get; set; } = "CRM";
    /// <summary>Base URL of the frontend; used to build email-confirm and password-reset links.</summary>
    public string AppUrl { get; set; } = "http://localhost:5173";
    public string SupportEmail { get; set; } = "support@crm.local";
}

public class CarrierOptions
{
    public Dictionary<string, CarrierEndpoint> Endpoints { get; set; } = new();
}

public class CarrierEndpoint
{
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 15;
}

public class FundingOptions
{
    public string Provider { get; set; } = "Stub";
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
}
