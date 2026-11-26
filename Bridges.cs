using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Athar
{
    /// <summary>The other side of a cross-chain transaction: where the value came out (or, for a payout, where it came from).</summary>
    public class BridgeHop
    {
        public string Service = "", Chain = "", ChainName = "", Address = "", Asset = "", Tx = "";
        public decimal Amount;
        public long Time;
        /// <summary>true: the queried transaction was the deposit and this is the payout side; false: the queried transaction was the payout and this is the origin.</summary>
        public bool Outbound = true;
    }

    /// <summary>Resolves bridge and cross-chain swap transactions through the services' own public lookups (THORChain Midgard, Relay, LI.FI).</summary>
    public static class Bridges
    {
        static string ChainFromId(long id) => id switch { 1 => "eth", 10 => "op", 56 => "bsc", 137 => "polygon", 324 => "zksync", 5000 => "mantle", 8453 => "base", 42161 => "arb", 43114 => "avax", 534352 => "scroll", 130 => "unichain", 42220 => "celo", 728126428 => "tron", 8253038 => "btc", 792703809 => "solana", 1151111081099710 => "solana", _ => "chain-" + id };
        static string ChainFromThor(string asset)
        {
            string c = (asset ?? "").Split('.', '~', '/', '-')[0].ToUpperInvariant();
            return c switch { "BTC" => "btc", "ETH" => "eth", "TRON" => "tron", "BSC" => "bsc", "AVAX" => "avax", "BASE" => "base", "XRP" => "xrp", "DOGE" => "doge", "LTC" => "ltc", "BCH" => "bch", "GAIA" => "cosmos", "THOR" => "thorchain", "SOL" => "solana", _ => c.ToLowerInvariant() };
        }
        public static string NameOf(string key) => Chains.All.FirstOrDefault(c => c.Key == key)?.Name ?? key switch { "solana" => "Solana", "xrp" => "XRP Ledger", "doge" => "Dogecoin", "ltc" => "Litecoin", "bch" => "Bitcoin Cash", "thorchain" => "THORChain", "cosmos" => "Cosmos", _ => key };

        public static async Task<List<BridgeHop>> Resolve(string chainKey, string hash, CancellationToken ct)
        {
            foreach (var f in new Func<string, string, CancellationToken, Task<List<BridgeHop>>>[] { Thor, Relay, LiFi })
            {
                try { var r = await f(chainKey, hash, ct); if (r.Count > 0) { foreach (var h in r) h.ChainName = NameOf(h.Chain); return r; } }
                catch (ApiException) { }
                catch (Exception e) when (e is FormatException || e is InvalidOperationException || e is KeyNotFoundException) { }
            }
            return new List<BridgeHop>();
        }

        const string Midgard = "https://gateway.liquify.com/chain/thorchain_midgard/v2/actions";

        /// <summary>Every THORChain swap a depositor made (up to 400), so that hundreds of small deposits resolve in a few calls.</summary>
        public static async Task<List<BridgeHop>> ThorAll(string depositor, long fromTime, CancellationToken ct)
        {
            var res = new List<BridgeHop>(); string token = "";
            for (int page = 0; page < 8; page++)
            {
                var j = await Http.Json($"{Midgard}?address={depositor}&type=swap&limit=50" + (token != "" ? "&nextPageToken=" + token : ""), 1800, ct, null, 2, 20);
                if (j == null) break;
                var acts = j.Value.Arr("actions").ToList();
                foreach (var a in acts)
                {
                    if (!a.Arr("in").Any(x => string.Equals(x.S("address"), depositor, StringComparison.OrdinalIgnoreCase))) continue;
                    res.AddRange(Sides(a, true, null));
                }
                token = j.Value.O("meta")?.S("nextPageToken") ?? "";
                if (acts.Count < 50 || token == "" || (fromTime > 0 && res.Count > 0 && res[^1].Time < fromTime)) break;
            }
            res.RemoveAll(h => string.Equals(h.Address, depositor, StringComparison.OrdinalIgnoreCase) || (fromTime > 0 && h.Time < fromTime));
            foreach (var h in res) h.ChainName = NameOf(h.Chain);
            return res;
        }

        static IEnumerable<BridgeHop> Sides(JsonElement a, bool isIn, string skipTx)
        {
            long time = 0; if (decimal.TryParse(a.S("date"), NumberStyles.Any, CultureInfo.InvariantCulture, out var ns)) time = (long)(ns / 1_000_000_000m);
            foreach (var side in a.Arr(isIn ? "out" : "in"))
            {
                var coin = side.Arr("coins").FirstOrDefault();
                if (coin.ValueKind != JsonValueKind.Object) continue;
                string asset = coin.S("asset");
                if (asset.StartsWith("THOR.") && isIn) continue;   // affiliate / fee legs paid in RUNE
                string tx = side.S("txID");
                string ck = ChainFromThor(asset);
                yield return new BridgeHop { Service = "THORChain", Chain = ck, Address = side.S("address"), Asset = asset.Split('.', '~')[^1].Split('-')[0], Amount = Units.From(coin.S("amount"), 8), Tx = ck == "eth" || ck == "bsc" || ck == "avax" || ck == "base" ? "0x" + tx.ToLowerInvariant() : tx.ToLowerInvariant(), Time = time, Outbound = isIn };
            }
        }

        static async Task<List<BridgeHop>> Thor(string chainKey, string hash, CancellationToken ct)
        {
            var res = new List<BridgeHop>();
            string id = hash.StartsWith("0x") ? hash.Substring(2) : hash;
            var j = await Http.Json(Midgard + "?txid=" + id.ToUpperInvariant(), 3600, ct, null, 2, 15);
            if (j == null) return res;
            foreach (var a in j.Value.Arr("actions"))
            {
                bool isIn = a.Arr("in").Any(x => string.Equals(x.S("txID"), id, StringComparison.OrdinalIgnoreCase));
                res.AddRange(Sides(a, isIn, null));
            }
            return res;
        }

        /// <summary>Relay's request lookup. Without a key only the older v2 endpoint answers (rate-limited and being retired); with a key from Settings v3 is used.</summary>
        static string RelayBase => "https://api.relay.link/requests/" + (string.IsNullOrWhiteSpace(Store.S?.RelayKey) ? "v2" : "v3");

        static BridgeHop RelayHop(JsonElement r, bool isIn)
        {
            var d = r.O("data"); if (d == null) return null;
            var other = d.Value.Arr(isIn ? "outTxs" : "inTxs").FirstOrDefault();
            if (other.ValueKind != JsonValueKind.Object) return null;
            var cur = d.Value.O("metadata")?.O(isIn ? "currencyOut" : "currencyIn");
            decimal.TryParse(cur?.S("amountFormatted") ?? "", NumberStyles.Any, CultureInfo.InvariantCulture, out decimal amt);
            return new BridgeHop { Service = "Relay", Chain = ChainFromId(other.L("chainId")), Address = isIn ? r.S("recipient") : r.S("user"), Asset = cur?.O("currency")?.S("symbol") ?? "", Amount = amt, Tx = other.S("hash"), Time = other.L("timestamp"), Outbound = isIn };
        }

        /// <summary>Every Relay request of a depositor (up to 300), so that dozens of deposits resolve in a few calls.</summary>
        public static async Task<List<BridgeHop>> RelayAll(string user, long fromTime, CancellationToken ct)
        {
            var res = new List<BridgeHop>(); string cont = "";
            for (int page = 0; page < 6; page++)
            {
                var j = await Http.Json($"{RelayBase}?user={user}&limit=50" + (cont != "" ? "&continuation=" + Uri.EscapeDataString(cont) : ""), 1800, ct, null, 3, 20);
                if (j == null) break;
                var reqs = j.Value.Arr("requests").ToList();
                foreach (var r in reqs)
                {
                    if (!string.Equals(r.S("user"), user, StringComparison.OrdinalIgnoreCase) || r.S("status") != "success") continue;
                    var h = RelayHop(r, true); if (h != null && h.Address != "") res.Add(h);
                }
                cont = j.Value.S("continuation");
                if (reqs.Count < 50 || cont == "") break;
            }
            res.RemoveAll(h => fromTime > 0 && h.Time > 0 && h.Time < fromTime);
            foreach (var h in res) h.ChainName = NameOf(h.Chain);
            return res;
        }

        static async Task<List<BridgeHop>> Relay(string chainKey, string hash, CancellationToken ct)
        {
            var res = new List<BridgeHop>();
            var j = await Http.Json(RelayBase + "?hash=" + hash, 3600, ct, null, string.IsNullOrWhiteSpace(Store.S?.RelayKey) ? 1 : 3, 15);
            if (j == null) return res;
            foreach (var r in j.Value.Arr("requests"))
            {
                var d = r.O("data"); if (d == null) continue;
                bool isIn = d.Value.Arr("inTxs").Any(t => string.Equals(t.S("hash"), hash, StringComparison.OrdinalIgnoreCase) || string.Equals("0x" + t.S("hash"), hash, StringComparison.OrdinalIgnoreCase));
                var h = RelayHop(r, isIn); if (h != null) res.Add(h);
            }
            return res;
        }

        static async Task<List<BridgeHop>> LiFi(string chainKey, string hash, CancellationToken ct)
        {
            var res = new List<BridgeHop>();
            if (!hash.StartsWith("0x")) return res;
            var j = await Http.Json("https://li.quest/v1/status?txHash=" + hash, 3600, ct, null, 1, 15);
            if (j == null) return res;
            var send = j.Value.O("sending"); var recv = j.Value.O("receiving");
            if (send == null || recv == null || recv.Value.S("txHash") == "") return res;
            bool isIn = string.Equals(send.Value.S("txHash"), hash, StringComparison.OrdinalIgnoreCase);
            var side = isIn ? recv.Value : send.Value;
            var tok = side.O("token");
            string ck = ChainFromId(side.L("chainId"));
            if (isIn && ck == chainKey && string.Equals(recv.Value.S("txHash"), hash, StringComparison.OrdinalIgnoreCase)) return res;   // same-chain swap, not a bridge
            res.Add(new BridgeHop { Service = "LI.FI" + (j.Value.S("tool") != "" ? " (" + j.Value.S("tool") + ")" : ""), Chain = ck, Address = isIn ? j.Value.S("toAddress") : j.Value.S("fromAddress"), Asset = tok?.S("symbol") ?? "", Amount = Units.From(side.S("amount"), (int)(tok?.L("decimals") ?? 18)), Tx = side.S("txHash"), Time = side.L("timestamp"), Outbound = isIn });
            return res;
        }
    }
}
