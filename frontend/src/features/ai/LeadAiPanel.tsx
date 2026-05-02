import { useAiLeadScoreQuery, useAiRecommendationsQuery, useRescoreLeadMutation } from "../../shared/api/baseApi";

export function LeadAiPanel({ leadId }: { leadId: string }) {
  const { data: recs } = useAiRecommendationsQuery(leadId);
  const { data: aiScore } = useAiLeadScoreQuery(leadId);
  const [rescore, { isLoading: rescoring }] = useRescoreLeadMutation();

  return (
    <div className="bg-white rounded shadow p-4 mb-4">
      <h3 className="font-semibold mb-2">AI insights</h3>

      <div className="flex items-center gap-3 mb-3">
        <div className="text-3xl font-bold text-brand-700">{aiScore?.score ?? "—"}</div>
        <div className="flex-1">
          <div className="text-xs text-slate-500">AI score</div>
          <div className="text-xs text-slate-600">{aiScore?.reasoning?.substring(0, 120)}</div>
        </div>
        <button className="text-xs bg-slate-100 rounded px-2 py-1"
          disabled={rescoring} onClick={() => rescore(leadId)}>
          {rescoring ? "..." : "Recalc heuristic"}
        </button>
      </div>

      <div>
        <div className="text-xs text-slate-500 mb-1">Recommended next actions</div>
        {recs?.items?.length ? (
          <ul className="space-y-1">
            {recs.items.map((r, i) => (
              <li key={i} className="text-sm flex items-center gap-2">
                <span className="text-xs bg-emerald-100 text-emerald-800 rounded px-2 py-0.5 font-medium">
                  {Math.round(r.confidence * 100)}%
                </span>
                <span><strong>{r.action}</strong> — <span className="text-slate-600">{r.reason}</span></span>
              </li>
            ))}
          </ul>
        ) : (
          <div className="text-sm text-slate-500">No recommendations.</div>
        )}
      </div>
    </div>
  );
}
