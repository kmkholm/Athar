using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;

namespace Athar
{
    /// <summary>Optional AI analysis through the Claude API (official Anthropic SDK). The user supplies the key in Settings; it is stored encrypted with Windows DPAPI.</summary>
    public static class Ai
    {
        public static readonly string[] Models = { "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5" };
        public static bool Ready => !string.IsNullOrWhiteSpace(Key);

        public static string Key
        {
            get
            {
                if (string.IsNullOrEmpty(Store.S.ClaudeKeyEnc)) return "";
                try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(Store.S.ClaudeKeyEnc), null, DataProtectionScope.CurrentUser)); } catch { return ""; }
            }
            set { Store.S.ClaudeKeyEnc = string.IsNullOrWhiteSpace(value) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser)); }
        }

        static string System()
        {
            string lang = L.Ar ? "Arabic (Modern Standard, clear and plain)" : "English";
            return "You assist a blockchain investigator who uses a tracing tool. You receive public on-chain data that the tool collected: address labels, transfers, heuristic findings and fund-flow traces.\n" +
                "Explain what the data shows and what to check next. Rules:\n" +
                "- Rely only on the data given. Separate what the data shows from what is a hypothesis, and say how strong each point is.\n" +
                "- An address is not a person. Never name or guess who owns an unlabelled address, and never state that someone committed a crime; describe behaviour and patterns instead.\n" +
                "- Labels come from explorers and may be wrong; say so when a conclusion depends on one.\n" +
                "- Quote addresses and transaction hashes exactly as given, in their shortened form when that is how they appear.\n" +
                "- If the data is partial (truncated history, limited hops), say what that leaves unknown.\n" +
                "- Give concrete next steps the investigator can do in the tool (trace forward or backward from an address, link addresses, match deposits and payouts of a bridge or mixer, watch an address, contact the exchange that received funds).\n" +
                "Write in " + lang + ". Plain text only: short headed sections and short lines starting with '- '. No Markdown tables, no bold markers, no code fences. Keep it under 350 words unless the data clearly needs more.";
        }

        public static async Task<string> Ask(string user, CancellationToken ct)
        {
            if (!Ready) throw new ApiException(L.T("Add your Claude API key in Settings to use AI analysis."));
            var client = new AnthropicClient { ApiKey = Key };
            string model = Models.Contains(Store.S.ClaudeModel) ? Store.S.ClaudeModel : Models[0];
            MessageCreateParams Build(bool fallback)
            {
                var p = new MessageCreateParams
                {
                    Model = model,
                    MaxTokens = 16000,
                    System = System(),
                    OutputConfig = new BetaOutputConfig { Effort = Effort.Medium },
                    Messages = [new BetaUserMessageParam { Content = user }],
                };
                // Server-side refusal fallback (beta): a declined request is re-served by a suitable model inside the same call.
                if (fallback) p = p with { Betas = ["server-side-fallback-2026-07-01"], Fallbacks = new Default() };
                return p;
            }
            BetaMessage r;
            try
            {
                bool fb = model != "claude-haiku-5-5";
                try { r = await client.Beta.Messages.Create(Build(fb), cancellationToken: ct); }
                catch (AnthropicBadRequestException e) when (fb && e.Message.Contains("fallback", StringComparison.OrdinalIgnoreCase)) { r = await client.Beta.Messages.Create(Build(false), cancellationToken: ct); }
            }
            catch (AnthropicUnauthorizedException) { throw new ApiException(L.T("The Claude API key was rejected. Check it in Settings.")); }
            catch (AnthropicRateLimitException) { throw new ApiException(L.T("Claude API rate limit reached. Try again in a minute.")); }
            catch (AnthropicApiException e) { throw new ApiException("Claude API: " + e.Message); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) when (e is not ApiException) { throw new ApiException("Claude API: " + e.Message); }
            if (r.StopReason == "refusal") throw new ApiException(L.T("The model declined to answer this request."));
            var sb = new StringBuilder();
            foreach (var b in r.Content) if (b.TryPickText(out var t)) sb.Append(t.Text);
            string text = sb.ToString().Trim();
            if (text == "") throw new ApiException(L.T("The model returned an empty answer."));
            return text;
        }

        static string Lab(string a) { var l = Labels.Find(a); return l != null && l.Name != "" ? $"{Fmt.Short(a)} [{l.Name}; {l.Cat}]" : Fmt.Short(a); }

        public static string AddressPrompt(Chain chain, string addr, AddrInfo info, TransferSet full, Xfer firstFunding = null)
        {
            const int cap = 160;
            var sb = new StringBuilder();
            var set = new TransferSet { Items = Tokens.Clean(full.Items), Truncated = full.Truncated, Note = full.Note };
            int spam = full.Items.Count - set.Items.Count;
            sb.AppendLine("TASK: Profile this address. Summarise its behaviour, list what looks unusual and why, and propose next steps.");
            sb.AppendLine($"NETWORK: {chain.Name}");
            sb.AppendLine($"ADDRESS: {addr}  (shown below as {Fmt.Short(addr)})");
            var own = Labels.Find(addr);
            sb.AppendLine($"LABEL: {(own != null ? own.Name + " (" + own.Cat + ", source: " + own.Source + ")" : "none")}; contract: {info.IsContract}; explorer scam flag: {info.IsScam}; tags: {string.Join(", ", info.Tags)}");
            sb.AppendLine($"BALANCE: {Fmt.Amt(info.Balance)} {chain.Symbol}; total transactions reported by explorer: {(info.TxCount >= 0 ? info.TxCount.ToString() : "unknown")}");
            sb.AppendLine($"TRANSFERS LOADED: {set.Items.Count}{(set.Truncated ? " (history is truncated: only the most recent transfers per type were loaded)" : " (complete for the loaded window)")}");
            sb.AppendLine("\nTOOL FINDINGS (heuristics, to be verified):");
            foreach (var f in Analysis.Findings(addr, info, full, firstFunding)) sb.AppendLine($"- [{f.Level}] {f.Title}: {f.Detail}");
            if (spam > 0) sb.AppendLine($"NOTE: {spam} transfers of spam or look-alike tokens were removed from everything below.");
            sb.AppendLine("\nTOP COUNTERPARTIES (counterparty | asset | received from it | sent to it | transfers | first..last UTC):");
            foreach (var c in Analysis.Counterparties(addr, set.Items).Take(25)) sb.AppendLine($"- {Lab(c.Address)} | {c.Asset} | {Fmt.Amt(c.In)} | {Fmt.Amt(c.Out)} | {c.Count} | {Fmt.Day(c.First)}..{Fmt.Day(c.Last)}");
            var hrs = Analysis.Hours(addr, set.Items);
            sb.AppendLine("\nOUTGOING TRANSACTIONS BY UTC HOUR (00..23): " + string.Join(" ", hrs));
            var shown = set.Items.OrderByDescending(x => x.Time).Take(cap).ToList();
            sb.AppendLine($"\nTRANSFERS, newest first ({shown.Count} of {set.Items.Count} loaded{(set.Items.Count > cap ? "; the rest are omitted from this message" : "")}) (time UTC | direction | counterparty | amount asset | type):");
            foreach (var x in shown) sb.AppendLine($"- {Fmt.Time(x.Time)} | {(x.From == addr ? "OUT" : "IN")} | {Lab(x.From == addr ? x.To : x.From)} | {Fmt.Amt(x.Amount)} {x.Asset} | {x.Kind}");
            return sb.ToString();
        }

        public static string TracePrompt(Graph g)
        {
            var sb = new StringBuilder();
            sb.AppendLine("TASK: Explain this fund-flow trace: the main path of the money, where it ended (exchanges, bridges, mixers, unexplored wallets), which endpoints matter most, how reliable each branch is, and the next steps.");
            sb.AppendLine($"NETWORK: {Chains.Get(g.Chain).Name}; DIRECTION: {(g.Forward ? "forward (where the money went)" : "backward (where the money came from)")}; START: {string.Join(", ", g.Nodes.Where(n => n.Depth == 0).Select(n => Lab(n.Id)))}; traced {Fmt.Time(g.Created)} UTC");
            sb.AppendLine($"PARAMETERS: {g.Options}");
            sb.AppendLine("METHOD: at each hop only the largest transfers made after the funds arrived are followed; the trace stops at known services, contracts and very active wallets. Later hops may include funds unrelated to the start.");
            sb.AppendLine("\nADDRESSES (hop | address [label; category] | why the trace stopped there):");
            foreach (var n in g.Nodes.OrderBy(n => n.Depth)) sb.AppendLine($"- {n.Depth} | {Lab(n.Id)} | status: {g.Status(n)}{(n.Frozen != "" ? " (blacklisted by " + n.Frozen + ", frozen balance " + GraphGeo.Money(n.FrozenUsd) + ")" : "")}; stop reason: {(n.Stop == "" ? (n.Expanded ? "expanded" : "not expanded") : n.Stop)}; balance now: {Fmt.Amt(n.Balance)}; value reached: {GraphGeo.Money(g.UsdIn(n))}");
            sb.AppendLine("\nFLOWS (from -> to | amount asset | transfers | first..last UTC):");
            foreach (var e in g.Edges.OrderBy(e => g.Node(e.From)?.Depth ?? 0).ThenByDescending(e => e.Amount)) sb.AppendLine($"- {Fmt.Short(e.From)} -> {Fmt.Short(e.To)} | {Fmt.Amt(e.Amount)} {e.Asset}{(e.Bridge ? " (payout of a bridge / cross-chain swap on " + Bridges.NameOf(e.ChainKey) + ")" : "")} | {e.Count} | {Fmt.Time(e.First)}..{Fmt.Time(e.Last)}");
            if (g.Notes.Count > 0) sb.AppendLine("\nTOOL NOTES: " + string.Join(" | ", g.Notes.Where(n => n != "nolabel").Distinct().Select(n => n == "relaykey" ? "funds entered the Relay bridge; the payout side could not be looked up without an API key" : n == "limit" ? "address limit reached, some branches not followed" : n == "recent" ? "start address is very active and no start date was given: only its most recent transfers were used" : n)));
            return sb.ToString();
        }

        public static string CasePrompt(CaseFile c)
        {
            var sb = new StringBuilder();
            sb.AppendLine("TASK: Draft the summary section of an investigation report for this case: what happened, the money trail, what is established and what is still open, and recommended next steps. Use only the material below.");
            sb.AppendLine($"CASE: {c.Name}; opened {Fmt.Day(c.Created)}");
            if (!string.IsNullOrWhiteSpace(c.Notes)) sb.AppendLine("\nINVESTIGATOR'S NOTES:\n" + c.Notes);
            if (c.Findings.Count > 0) { sb.AppendLine("\nINVESTIGATOR'S FINDINGS:"); foreach (var f in c.Findings) sb.AppendLine("- " + f); }
            if (c.Items.Count > 0) { sb.AppendLine("\nITEMS (kind | network | value | role | note):"); foreach (var i in c.Items) sb.AppendLine($"- {i.Kind} | {i.Chain} | {(i.Kind == "address" ? Lab(i.Value) : Fmt.Short(i.Value))} | {i.Role} | {i.Note}"); }
            int t = 1;
            foreach (var g in c.Traces) { sb.AppendLine($"\n===== TRACE {t++} ====="); sb.AppendLine(TracePrompt(g)); }
            return sb.ToString();
        }
    }
}
