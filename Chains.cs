using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Athar
{
    public interface IChainApi
    {
        Task<AddrInfo> Info(string address, CancellationToken ct);
        /// <summary>Value transfers touching the address. asc=true returns the oldest first (from fromTime); otherwise newest first (up to toTime).</summary>
        Task<TransferSet> Transfers(string address, int max, bool asc, long fromTime, long toTime, CancellationToken ct);
        Task<List<Xfer>> Tx(string hash, CancellationToken ct);
    }

    public class Chain
    {
        public string Key, Name, Symbol, Family, Explorer;
        public bool NeedsKey;
        public int ChainId;
        public Func<Chain, IChainApi> Make;
        IChainApi api;
        public IChainApi Api => api ??= Make(this);
        public void Reset() => api = null;
        public bool Ready => !NeedsKey || !string.IsNullOrWhiteSpace(Store.S?.EtherscanKey);
        public string AddrUrl(string a) => Family == "btc" ? $"{Explorer}/address/{a}" : Family == "tron" ? $"{Explorer}/#/address/{a}" : $"{Explorer}/address/{a}";
        public string TxUrl(string h) => Family == "btc" ? $"{Explorer}/tx/{h}" : Family == "tron" ? $"{Explorer}/#/transaction/{h}" : $"{Explorer}/tx/{h}";
        public override string ToString() => Name;
    }

    public static class Chains
    {
        public static readonly List<Chain> All = new()
        {
            new Chain { Key = "eth", Name = "Ethereum", Symbol = "ETH", Family = "evm", ChainId = 1, Explorer = "https://etherscan.io", Make = c => new BlockscoutApi(c, "https://eth.blockscout.com", string.IsNullOrWhiteSpace(Store.S.EtherscanKey) ? "https://api.routescan.io/v2/network/mainnet/evm/1/etherscan/api?" : EtherscanApi.V2Base(c)) },
            new Chain { Key = "btc", Name = "Bitcoin", Symbol = "BTC", Family = "btc", Explorer = "https://mempool.space", Make = c => new BtcApi(c) },
            new Chain { Key = "tron", Name = "Tron", Symbol = "TRX", Family = "tron", Explorer = "https://tronscan.org", Make = c => new TronApi(c) },
            new Chain { Key = "op", Name = "Optimism", Symbol = "ETH", Family = "evm", ChainId = 10, Explorer = "https://optimistic.etherscan.io", Make = c => new BlockscoutApi(c, "https://explorer.optimism.io") },
            new Chain { Key = "zksync", Name = "zkSync Era", Symbol = "ETH", Family = "evm", ChainId = 324, Explorer = "https://explorer.zksync.io", Make = c => new BlockscoutApi(c, "https://zksync.blockscout.com") },
            new Chain { Key = "scroll", Name = "Scroll", Symbol = "ETH", Family = "evm", ChainId = 534352, Explorer = "https://scrollscan.com", Make = c => new BlockscoutApi(c, "https://scroll.blockscout.com") },
            new Chain { Key = "unichain", Name = "Unichain", Symbol = "ETH", Family = "evm", ChainId = 130, Explorer = "https://uniscan.xyz", Make = c => new BlockscoutApi(c, "https://unichain.blockscout.com") },
            new Chain { Key = "celo", Name = "Celo", Symbol = "CELO", Family = "evm", ChainId = 42220, Explorer = "https://celoscan.io", Make = c => new BlockscoutApi(c, "https://celo.blockscout.com") },
            new Chain { Key = "mantle", Name = "Mantle", Symbol = "MNT", Family = "evm", ChainId = 5000, Explorer = "https://mantlescan.xyz", Make = c => new EtherscanApi(c, "https://api.routescan.io/v2/network/mainnet/evm/5000/etherscan/api?") },
            new Chain { Key = "avax", Name = "Avalanche C", Symbol = "AVAX", Family = "evm", ChainId = 43114, Explorer = "https://snowtrace.io", Make = c => new EtherscanApi(c, "https://api.routescan.io/v2/network/mainnet/evm/43114/etherscan/api?") },
            new Chain { Key = "base", Name = "Base", Symbol = "ETH", Family = "evm", ChainId = 8453, NeedsKey = true, Explorer = "https://basescan.org", Make = EtherscanApi.V2 },
            new Chain { Key = "arb", Name = "Arbitrum One", Symbol = "ETH", Family = "evm", ChainId = 42161, NeedsKey = true, Explorer = "https://arbiscan.io", Make = EtherscanApi.V2 },
            new Chain { Key = "polygon", Name = "Polygon", Symbol = "POL", Family = "evm", ChainId = 137, NeedsKey = true, Explorer = "https://polygonscan.com", Make = EtherscanApi.V2 },
            new Chain { Key = "bsc", Name = "BNB Chain", Symbol = "BNB", Family = "evm", ChainId = 56, NeedsKey = true, Explorer = "https://bscscan.com", Make = EtherscanApi.V2 },
        };
        public static Chain Get(string key) => All.FirstOrDefault(c => c.Key == key) ?? throw new ApiException("Unknown chain: " + key);
        public static void ResetAll() { foreach (var c in All) c.Reset(); }

        /// <summary>Stablecoin contracts that can safely be valued at 1 USD (lower-case / base58).</summary>
        public static readonly HashSet<string> Stable = new()
        {
            "0xdac17f958d2ee523a2206206994597c13d831ec7", "0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48", "0x6b175474e89094c44da98b954eedeac495271d0f",
            "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", "TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8",
        };
        public static readonly Dictionary<string, (string sym, int dec)> KnownTokens = new()
        {
            ["0xdac17f958d2ee523a2206206994597c13d831ec7"] = ("USDT", 6), ["0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48"] = ("USDC", 6),
            ["0x6b175474e89094c44da98b954eedeac495271d0f"] = ("DAI", 18), ["0xc02aaa39b223fe8d0a0e5c4f27ead9083c756cc2"] = ("WETH", 18),
            ["TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t"] = ("USDT", 6), ["TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8"] = ("USDC", 6),
        };
    }

    /// <summary>Etherscan-compatible list endpoints shared by Blockscout, Routescan and Etherscan V2.</summary>
    public abstract class EvmListApi : IChainApi, IFreezeCheck
    {
        /// <summary>USDT isBlackListed(address) and USDC isBlacklisted(address) on Ethereum.</summary>
        public async Task<List<string>> Frozen(string address, CancellationToken ct)
        {
            var res = new List<string>();
            if (C.Key != "eth") return res;
            string arg = "000000000000000000000000" + address.ToLowerInvariant().Substring(2);
            foreach (var (token, sel, name) in new[] { ("0xdac17f958d2ee523a2206206994597c13d831ec7", "0xe47d6060", "USDT"), ("0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48", "0xfe575a87", "USDC") })
            {
                var j = await Http.Json(Url($"module=proxy&action=eth_call&to={token}&data={sel}{arg}&tag=latest"), 600, ct, null, 2, 15);
                string r = j?.S("result") ?? "";
                if (r.StartsWith("0x") && r.Length >= 66 && r.EndsWith("1")) res.Add(name);
            }
            return res;
        }

        protected readonly Chain C;
        protected EvmListApi(Chain c) { C = c; }
        public async Task<decimal> FrozenAmount(string address, List<string> tokens, CancellationToken ct)
        {
            decimal sum = 0; string arg = "000000000000000000000000" + address.ToLowerInvariant().Substring(2);
            foreach (var (token, name) in new[] { ("0xdac17f958d2ee523a2206206994597c13d831ec7", "USDT"), ("0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48", "USDC") })
            {
                if (!tokens.Contains(name)) continue;
                var j = await Http.Json(Url($"module=proxy&action=eth_call&to={token}&data=0x70a08231{arg}&tag=latest"), 600, ct, null, 2, 15);
                sum += Units.FromHex(j?.S("result") ?? "", 6);
            }
            return sum;
        }
        protected abstract string Url(string query);
        public abstract Task<AddrInfo> Info(string address, CancellationToken ct);
        public abstract Task<List<Xfer>> Tx(string hash, CancellationToken ct);

        protected async Task<List<JsonElement>> List(string query, CancellationToken ct)
        {
            var j = await Http.Json(Url(query), Store.S.CacheSec, ct);
            if (j == null) return new();
            var r = j.Value.O("result");
            if (r.HasValue && r.Value.ValueKind == JsonValueKind.Array) return r.Value.EnumerateArray().ToList();
            string msg = j.Value.S("message") + " " + (r.HasValue && r.Value.ValueKind == JsonValueKind.String ? r.Value.GetString() : "");
            if (msg.Contains("No ", StringComparison.OrdinalIgnoreCase) && msg.Contains("found", StringComparison.OrdinalIgnoreCase)) return new();
            if (string.IsNullOrWhiteSpace(msg) || msg.Trim() == "OK") return new();
            throw new ApiException(C.Name + ": " + msg.Trim());
        }

        protected async Task<long> BlockAt(long time, bool before, CancellationToken ct)
        {
            try
            {
                var j = await Http.Json(Url($"module=block&action=getblocknobytime&timestamp={time}&closest={(before ? "before" : "after")}"), 86400, ct);
                var r = j?.O("result");
                if (r == null) return -1;
                if (r.Value.ValueKind == JsonValueKind.String) return long.TryParse(r.Value.GetString(), out long b) ? b : -1;
                return r.Value.L("blockNumber");
            }
            catch (ApiException) { return -1; }
        }

        public virtual async Task<TransferSet> Transfers(string address, int max, bool asc, long fromTime, long toTime, CancellationToken ct)
        {
            address = address.ToLowerInvariant();
            string range = "";
            if (fromTime > 0) { long b = await BlockAt(fromTime, false, ct); if (b > 0) range += "&startblock=" + b; }
            if (toTime > 0) { long b = await BlockAt(toTime, true, ct); if (b > 0) range += "&endblock=" + b; }
            string tail = $"&address={address}{range}&page=1&offset={max}&sort={(asc ? "asc" : "desc")}";
            var tNorm = List("module=account&action=txlist" + tail, ct);
            var tTok = List("module=account&action=tokentx" + tail, ct);
            var tInt = SafeList("module=account&action=txlistinternal" + tail, ct);
            await Task.WhenAll(tNorm, tTok, tInt);
            var set = new TransferSet();
            foreach (var e in tNorm.Result)
            {
                if (e.S("isError") == "1") continue;
                decimal v = Units.From(e.S("value"), 18);
                if (v == 0) continue;
                string to = e.S("to"); if (to == "") to = e.S("contractAddress");
                set.Items.Add(new Xfer { Chain = C.Key, Hash = e.S("hash"), From = e.S("from").ToLowerInvariant(), To = to.ToLowerInvariant(), Asset = C.Symbol, Kind = "native", Amount = v, Time = e.L("timeStamp"), Block = e.L("blockNumber") });
            }
            foreach (var e in tTok.Result)
            {
                string raw = e.S("value"); if (raw == "") continue;
                int dec = (int)e.L("tokenDecimal");
                decimal v = Units.From(raw, dec);
                if (v == 0) continue;
                string sym = e.S("tokenSymbol"); if (sym == "") sym = "TOKEN";
                if (sym.Length > 14) sym = sym.Substring(0, 14);
                set.Items.Add(new Xfer { Chain = C.Key, Hash = e.S("hash"), From = e.S("from").ToLowerInvariant(), To = e.S("to").ToLowerInvariant(), Asset = sym, Contract = e.S("contractAddress").ToLowerInvariant(), Kind = "token", Amount = v, Time = e.L("timeStamp"), Block = e.L("blockNumber") });
            }
            foreach (var e in tInt.Result)
            {
                if (e.S("isError") == "1") continue;
                decimal v = Units.From(e.S("value"), 18);
                if (v == 0) continue;
                string h = e.S("transactionHash"); if (h == "") h = e.S("hash");
                set.Items.Add(new Xfer { Chain = C.Key, Hash = h, From = e.S("from").ToLowerInvariant(), To = e.S("to").ToLowerInvariant(), Asset = C.Symbol, Kind = "internal", Amount = v, Time = e.L("timeStamp"), Block = e.L("blockNumber") });
            }
            set.Truncated = tNorm.Result.Count >= max || tTok.Result.Count >= max || tInt.Result.Count >= max;
            set.Items = (asc ? set.Items.OrderBy(x => x.Time) : set.Items.OrderByDescending(x => x.Time)).ToList();
            if (fromTime > 0) set.Items.RemoveAll(x => x.Time < fromTime);
            if (toTime > 0) set.Items.RemoveAll(x => x.Time > toTime);
            return set;
        }

        async Task<List<JsonElement>> SafeList(string q, CancellationToken ct)
        {
            try { return await List(q, ct); } catch (ApiException) { return new(); }
        }
    }

    public class BlockscoutApi : EvmListApi
    {
        readonly string host, listBase;
        /// <summary>Address info, labels and single transactions come from Blockscout's REST API; the transfer lists come from an Etherscan-compatible endpoint (Blockscout's own one allows only a handful of calls without a key).</summary>
        public BlockscoutApi(Chain c, string host, string listBase = null) : base(c) { this.host = host; this.listBase = listBase; }
        protected override string Url(string query) => listBase != null ? listBase + query : host + "/api?" + query;

        public static void ReadTags(JsonElement a, AddrInfo info)
        {
            if (a.S("name") != "") info.Name = a.S("name");
            if (a.S("ens_domain_name") != "") info.Tags.Add(a.S("ens_domain_name"));
            foreach (var t in a.Arr("public_tags")) { string n = t.S("display_name"); if (n != "") info.Tags.Add(n); }
            var md = a.O("metadata");
            if (md.HasValue)
                foreach (var t in md.Value.Arr("tags").OrderByDescending(t => t.L("ordinal")))
                {
                    string type = t.S("tagType"), n = t.S("name");
                    if (type == "note" || n == "" || n.StartsWith("Proxy_")) continue;
                    if (type == "name" && info.Name == "") info.Name = n;
                    else if (!info.Tags.Contains(n)) info.Tags.Add(n);
                }
            info.IsContract = a.B("is_contract");
            info.IsScam = a.B("is_scam");
        }

        /// <summary>One list of Blockscout's REST API, newest first, following next_page_params.</summary>
        async Task<(List<JsonElement> items, bool truncated)> Pages(string address, string path, int max, long fromTime, CancellationToken ct)
        {
            var all = new List<JsonElement>(); string q = ""; int maxPages = Math.Clamp(max / 50 + 1, 1, 40);
            for (int page = 0; page < maxPages; page++)
            {
                string url = $"{host}/api/v2/addresses/{address}/{path}" + (q == "" ? "" : (path.Contains('?') ? "&" : "?") + q);
                JsonElement? j;
                try { j = await Http.Json(url, Store.S.CacheSec, ct, null, 2, 20); }
                catch (ApiException) when (page > 0 || path.StartsWith("internal")) { return (all, true); }
                if (j == null) return (all, false);
                var items = j.Value.Arr("items").ToList();
                all.AddRange(items);
                var np = j.Value.O("next_page_params");
                if (np == null || np.Value.ValueKind != JsonValueKind.Object || items.Count == 0) return (all, false);
                if (fromTime > 0 && Fmt.ParseDate(items[items.Count - 1].S("timestamp")) < fromTime) return (all, false);
                q = string.Join("&", np.Value.EnumerateObject().Where(p => p.Value.ValueKind != JsonValueKind.Null).Select(p => p.Name + "=" + Uri.EscapeDataString(p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText())));
            }
            return (all, true);
        }

        public override async Task<TransferSet> Transfers(string address, int max, bool asc, long fromTime, long toTime, CancellationToken ct)
        {
            if (listBase != null) return await base.Transfers(address, max, asc, fromTime, toTime, ct);
            address = address.ToLowerInvariant();
            var set = new TransferSet();
            string H(JsonElement e, string k) => e.O(k)?.S("hash").ToLowerInvariant() ?? "";
            var tx = await Pages(address, "transactions", max, fromTime, ct);
            foreach (var e in tx.items)
            {
                if (e.S("status") == "error") continue;
                decimal v = Units.From(e.S("value"), 18); if (v == 0) continue;
                set.Items.Add(new Xfer { Chain = C.Key, Hash = e.S("hash"), From = H(e, "from"), To = H(e, "to"), Asset = C.Symbol, Kind = "native", Amount = v, Time = Fmt.ParseDate(e.S("timestamp")), Block = e.L("block_number") });
            }
            var tok = await Pages(address, "token-transfers?type=ERC-20", max, fromTime, ct);
            foreach (var e in tok.items)
            {
                var t = e.O("token"); var tot = e.O("total"); if (t == null || tot == null) continue;
                int dec = (int)tot.Value.L("decimals"); if (dec == 0) dec = (int)t.Value.L("decimals");
                decimal v = Units.From(tot.Value.S("value"), dec); if (v == 0) continue;
                string sym = t.Value.S("symbol"); if (sym == "") sym = "TOKEN"; if (sym.Length > 14) sym = sym.Substring(0, 14);
                string c = t.Value.S("address_hash"); if (c == "") c = t.Value.S("address");
                set.Items.Add(new Xfer { Chain = C.Key, Hash = e.S("transaction_hash"), From = H(e, "from"), To = H(e, "to"), Asset = sym, Contract = c.ToLowerInvariant(), Kind = "token", Amount = v, Time = Fmt.ParseDate(e.S("timestamp")), Block = e.L("block_number") });
            }
            var itx = await Pages(address, "internal-transactions", Math.Min(max, 500), fromTime, ct);
            foreach (var e in itx.items)
            {
                if (e.O("success").HasValue && !e.B("success")) continue;
                string type = e.S("type"); if (type != "" && type != "call" && type != "create" && type != "selfdestruct") continue;
                decimal v = Units.From(e.S("value"), 18); if (v == 0) continue;
                set.Items.Add(new Xfer { Chain = C.Key, Hash = e.S("transaction_hash"), From = H(e, "from"), To = H(e, "to"), Asset = C.Symbol, Kind = "internal", Amount = v, Time = Fmt.ParseDate(e.S("timestamp")), Block = e.L("block_number") });
            }
            set.Truncated = tx.truncated || tok.truncated || itx.truncated;
            if (fromTime > 0) set.Items.RemoveAll(x => x.Time < fromTime);
            if (toTime > 0) set.Items.RemoveAll(x => x.Time > toTime);
            set.Items = (asc ? set.Items.OrderBy(x => x.Time) : set.Items.OrderByDescending(x => x.Time)).ToList();
            return set;
        }

        public override async Task<AddrInfo> Info(string address, CancellationToken ct)
        {
            var info = new AddrInfo { Chain = C.Key, Address = address.ToLowerInvariant() };
            var a = await Http.Json($"{host}/api/v2/addresses/{address}", Store.S.CacheSec, ct, null, 3, 15);
            if (a == null) { info.Note = "No activity recorded for this address."; return info; }
            ReadTags(a.Value, info);
            info.Balance = Units.From(a.Value.S("coin_balance"), 18);
            decimal.TryParse(a.Value.S("exchange_rate"), NumberStyles.Any, CultureInfo.InvariantCulture, out var rate);
            info.Usd = rate;
            try
            {
                var c = await Http.Json($"{host}/api/v2/addresses/{address}/counters", Store.S.CacheSec, ct, null, 1, 8);
                if (c != null) info.TxCount = c.Value.L("transactions_count") + c.Value.L("token_transfers_count");
            }
            catch (ApiException) { }
            return info;
        }

        public override async Task<List<Xfer>> Tx(string hash, CancellationToken ct)
        {
            var list = new List<Xfer>();
            var t = await Http.Json($"{host}/api/v2/transactions/{hash}", Store.S.CacheSec, ct);
            if (t == null) throw new ApiException("Transaction not found on " + C.Name);
            long time = Fmt.ParseDate(t.Value.S("timestamp"));
            long block = t.Value.L("block_number");
            string from = t.Value.O("from")?.S("hash").ToLowerInvariant() ?? "", to = t.Value.O("to")?.S("hash").ToLowerInvariant() ?? "";
            decimal v = Units.From(t.Value.S("value"), 18);
            if (v > 0) list.Add(new Xfer { Chain = C.Key, Hash = hash, From = from, To = to, Asset = C.Symbol, Amount = v, Time = time, Block = block });
            var tt = await Http.Json($"{host}/api/v2/transactions/{hash}/token-transfers", Store.S.CacheSec, ct);
            if (tt != null)
                foreach (var e in tt.Value.Arr("items"))
                {
                    var tok = e.O("token"); var tot = e.O("total");
                    if (tok == null || tot == null) continue;
                    int dec = (int)tot.Value.L("decimals"); if (dec == 0) dec = (int)tok.Value.L("decimals");
                    decimal amt = Units.From(tot.Value.S("value"), dec);
                    if (amt == 0) continue;
                    string c = tok.Value.S("address_hash"); if (c == "") c = tok.Value.S("address");
                    string sym = tok.Value.S("symbol"); if (sym == "") sym = "TOKEN";
                    list.Add(new Xfer { Chain = C.Key, Hash = hash, From = e.O("from")?.S("hash").ToLowerInvariant() ?? "", To = e.O("to")?.S("hash").ToLowerInvariant() ?? "", Asset = sym, Contract = c.ToLowerInvariant(), Kind = "token", Amount = amt, Time = time, Block = block });
                }
            try
            {
                var it = await Http.Json($"{host}/api/v2/transactions/{hash}/internal-transactions", Store.S.CacheSec, ct);
                if (it != null)
                    foreach (var e in it.Value.Arr("items"))
                    {
                        decimal amt = Units.From(e.S("value"), 18);
                        if (amt == 0 || (e.O("success").HasValue && !e.B("success"))) continue;
                        list.Add(new Xfer { Chain = C.Key, Hash = hash, From = e.O("from")?.S("hash").ToLowerInvariant() ?? "", To = e.O("to")?.S("hash").ToLowerInvariant() ?? "", Asset = C.Symbol, Kind = "internal", Amount = amt, Time = time, Block = block });
                    }
            }
            catch (ApiException) { }
            if (list.Count == 0 && from != "") list.Add(new Xfer { Chain = C.Key, Hash = hash, From = from, To = to, Asset = C.Symbol, Amount = 0, Time = time, Block = block });
            return list;
        }
    }

    public class EtherscanApi : EvmListApi
    {
        readonly string baseUrl;
        public EtherscanApi(Chain c, string baseUrl) : base(c) { this.baseUrl = baseUrl; }
        public static string V2Base(Chain c) => $"https://api.etherscan.io/v2/api?chainid={c.ChainId}&apikey={Uri.EscapeDataString(Store.S.EtherscanKey ?? "")}&";
        public static IChainApi V2(Chain c) => new EtherscanApi(c, V2Base(c));
        protected override string Url(string query) => baseUrl + query;

        async Task<JsonElement?> Proxy(string q, CancellationToken ct)
        {
            var j = await Http.Json(Url("module=proxy&" + q), Store.S.CacheSec, ct);
            return j?.O("result");
        }

        public override async Task<AddrInfo> Info(string address, CancellationToken ct)
        {
            if (C.NeedsKey && !C.Ready) throw new ApiException(C.Name + ": add an Etherscan API key in Settings to use this chain.");
            var info = new AddrInfo { Chain = C.Key, Address = address.ToLowerInvariant() };
            var j = await Http.Json(Url($"module=account&action=balance&address={address}&tag=latest"), Store.S.CacheSec, ct);
            var r = j?.O("result");
            if (r.HasValue && r.Value.ValueKind == JsonValueKind.String) info.Balance = Units.From(r.Value.GetString(), 18);
            try
            {
                var code = await Proxy($"action=eth_getCode&address={address}&tag=latest", ct);
                info.IsContract = code.HasValue && code.Value.ValueKind == JsonValueKind.String && code.Value.GetString().Length > 2;
                var n = await Proxy($"action=eth_getTransactionCount&address={address}&tag=latest", ct);
                if (n.HasValue && n.Value.ValueKind == JsonValueKind.String) info.TxCount = Units.HexLong(n.Value.GetString());
            }
            catch (Exception e) when (e is ApiException || e is FormatException) { }
            return info;
        }

        public override async Task<List<Xfer>> Tx(string hash, CancellationToken ct)
        {
            if (C.NeedsKey && !C.Ready) throw new ApiException(C.Name + ": add an Etherscan API key in Settings to use this chain.");
            var list = new List<Xfer>();
            var t = await Proxy($"action=eth_getTransactionByHash&txhash={hash}", ct);
            if (t == null || t.Value.ValueKind != JsonValueKind.Object) throw new ApiException("Transaction not found on " + C.Name);
            string bn = t.Value.S("blockNumber");
            long time = 0, block = Units.HexLong(bn);
            var blk = await Proxy($"action=eth_getBlockByNumber&tag={bn}&boolean=false", ct);
            if (blk.HasValue && blk.Value.ValueKind == JsonValueKind.Object) time = Units.HexLong(blk.Value.S("timestamp"));
            string from = t.Value.S("from").ToLowerInvariant(), to = t.Value.S("to").ToLowerInvariant();
            decimal v = Units.FromHex(t.Value.S("value"), 18);
            if (v > 0) list.Add(new Xfer { Chain = C.Key, Hash = hash, From = from, To = to, Asset = C.Symbol, Amount = v, Time = time, Block = block });
            var rc = await Proxy($"action=eth_getTransactionReceipt&txhash={hash}", ct);
            if (rc.HasValue && rc.Value.ValueKind == JsonValueKind.Object)
                foreach (var lg in rc.Value.Arr("logs"))
                {
                    var tp = lg.Arr("topics").Select(x => x.GetString()).ToList();
                    if (tp.Count != 3 || tp[0] != "0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef") continue;
                    string c = lg.S("address").ToLowerInvariant();
                    bool known = Chains.KnownTokens.TryGetValue(c, out var k);
                    decimal amt = Units.FromHex(lg.S("data"), known ? k.dec : 18);
                    if (amt == 0) continue;
                    list.Add(new Xfer { Chain = C.Key, Hash = hash, From = "0x" + tp[1].Substring(26), To = "0x" + tp[2].Substring(26), Asset = known ? k.sym : "token " + Fmt.Short(c) + " (18 dec. assumed)", Contract = c, Kind = "token", Amount = amt, Time = time, Block = block });
                }
            if (list.Count == 0) list.Add(new Xfer { Chain = C.Key, Hash = hash, From = from, To = to, Asset = C.Symbol, Amount = 0, Time = time, Block = block });
            return list;
        }
    }

    public class BtcApi : IChainApi
    {
        readonly Chain C; const string H = "https://mempool.space/api";
        public BtcApi(Chain c) { C = c; }

        public async Task<AddrInfo> Info(string address, CancellationToken ct)
        {
            var info = new AddrInfo { Chain = C.Key, Address = address };
            var a = await Http.Json($"{H}/address/{address}", Store.S.CacheSec, ct);
            if (a == null) throw new ApiException("Bitcoin address not found");
            var cs = a.Value.O("chain_stats"); var ms = a.Value.O("mempool_stats");
            if (cs.HasValue)
            {
                info.Balance = (cs.Value.L("funded_txo_sum") - cs.Value.L("spent_txo_sum") + (ms?.L("funded_txo_sum") ?? 0) - (ms?.L("spent_txo_sum") ?? 0)) / 100000000m;
                info.TxCount = cs.Value.L("tx_count");
            }
            try { var p = await Http.Json($"{H}/v1/prices", 900, ct); if (p != null) info.Usd = p.Value.L("USD"); } catch (ApiException) { }
            return info;
        }

        static List<Xfer> Normalize(JsonElement tx, string focus, string chain)
        {
            var res = new List<Xfer>();
            var ins = new Dictionary<string, long>();
            bool coinbase = false;
            foreach (var vin in tx.Arr("vin"))
            {
                if (vin.B("is_coinbase")) { coinbase = true; continue; }
                var po = vin.O("prevout"); if (po == null) continue;
                string a = po.Value.S("scriptpubkey_address"); if (a == "") continue;
                ins[a] = ins.GetValueOrDefault(a) + po.Value.L("value");
            }
            var st = tx.O("status");
            long time = st?.L("block_time") ?? 0; if (time == 0) time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long block = st?.L("block_height") ?? 0;
            string hash = tx.S("txid");
            string mainIn = coinbase ? "coinbase" : ins.Count > 0 ? ins.OrderByDescending(k => k.Value).First().Key : "unknown";
            var outs = new Dictionary<string, long>();
            foreach (var vo in tx.Arr("vout")) { string a = vo.S("scriptpubkey_address"); if (a == "") continue; outs[a] = outs.GetValueOrDefault(a) + vo.L("value"); }
            if (focus == null || ins.ContainsKey(focus))
            {
                string from = focus ?? mainIn;
                foreach (var o in outs) if (!ins.ContainsKey(o.Key)) res.Add(new Xfer { Chain = chain, Hash = hash, From = from, To = o.Key, Asset = "BTC", Amount = o.Value / 100000000m, Time = time, Block = block });
            }
            else if (outs.TryGetValue(focus, out long v))
                res.Add(new Xfer { Chain = chain, Hash = hash, From = mainIn, To = focus, Asset = "BTC", Amount = v / 100000000m, Time = time, Block = block });
            return res;
        }

        public async Task<TransferSet> Transfers(string address, int max, bool asc, long fromTime, long toTime, CancellationToken ct)
        {
            var set = new TransferSet();
            string last = null; int pages = 0, maxPages = Math.Clamp(max / 25 + 1, 1, 40);
            while (pages < maxPages)
            {
                var j = await Http.Json(last == null ? $"{H}/address/{address}/txs" : $"{H}/address/{address}/txs/chain/{last}", Store.S.CacheSec, ct);
                var arr = j.Arr().ToList();
                if (arr.Count == 0) break;
                long oldest = long.MaxValue;
                foreach (var tx in arr) { foreach (var x in Normalize(tx, address, C.Key)) { set.Items.Add(x); oldest = Math.Min(oldest, x.Time); } last = tx.S("txid"); }
                pages++;
                if (arr.Count < 25 || (fromTime > 0 && oldest < fromTime)) break;
                if (pages == maxPages) set.Truncated = true;
            }
            if (fromTime > 0) set.Items.RemoveAll(x => x.Time < fromTime);
            if (toTime > 0) set.Items.RemoveAll(x => x.Time > toTime);
            set.Items = (asc ? set.Items.OrderBy(x => x.Time) : set.Items.OrderByDescending(x => x.Time)).ToList();
            if (set.Truncated) set.Note = "Bitcoin history is read newest-first; older transactions beyond the page limit were not loaded.";
            return set;
        }

        public async Task<List<Xfer>> Tx(string hash, CancellationToken ct)
        {
            var j = await Http.Json($"{H}/tx/{hash}", Store.S.CacheSec, ct);
            if (j == null || j.Value.ValueKind != JsonValueKind.Object) throw new ApiException("Transaction not found on Bitcoin");
            return Normalize(j.Value, null, C.Key);
        }
    }

    public class TronApi : IChainApi, IFreezeCheck
    {
        public async Task<decimal> FrozenAmount(string address, List<string> tokens, CancellationToken ct)
        {
            string hex = Addr.TronToHex(address); if (hex == "") return 0;
            string body = "{\"owner_address\":\"" + address + "\",\"contract_address\":\"TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t\",\"function_selector\":\"balanceOf(address)\",\"parameter\":\"000000000000000000000000" + hex + "\",\"visible\":true}";
            var j = await Http.Json($"{H}/wallet/triggerconstantcontract", 600, ct, body, 2, 15);
            return Units.FromHex(j?.Arr("constant_result").Select(x => x.GetString()).FirstOrDefault() ?? "", 6);
        }
        /// <summary>USDT isBlackListed(address) on Tron.</summary>
        public async Task<List<string>> Frozen(string address, CancellationToken ct)
        {
            var res = new List<string>();
            string hex = Addr.TronToHex(address); if (hex == "") return res;
            string body = "{\"owner_address\":\"" + address + "\",\"contract_address\":\"TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t\",\"function_selector\":\"isBlackListed(address)\",\"parameter\":\"000000000000000000000000" + hex + "\",\"visible\":true}";
            var j = await Http.Json($"{H}/wallet/triggerconstantcontract", 600, ct, body, 2, 15);
            string r = j?.Arr("constant_result").Select(x => x.GetString()).FirstOrDefault() ?? "";
            if (r.Length >= 64 && r.EndsWith("1")) res.Add("USDT");
            return res;
        }

        readonly Chain C; const string H = "https://api.trongrid.io";
        public TronApi(Chain c) { C = c; }

        public async Task<AddrInfo> Info(string address, CancellationToken ct)
        {
            var info = new AddrInfo { Chain = C.Key, Address = address };
            var j = await Http.Json($"{H}/v1/accounts/{address}", Store.S.CacheSec, ct);
            var d = j?.Arr("data").FirstOrDefault();
            if (d.HasValue && d.Value.ValueKind == JsonValueKind.Object) info.Balance = d.Value.L("balance") / 1000000m;
            else info.Note = "Account not activated or no data.";
            if (Chains.KnownTokens.TryGetValue(address, out var k)) { info.Name = k.sym + " token contract"; info.IsContract = true; }
            try { var p = await Http.Json("https://api.coingecko.com/api/v3/simple/price?ids=tron&vs_currencies=usd", 1800, ct, null, 1, 10); if (p?.O("tron") is JsonElement tr && decimal.TryParse(tr.S("usd"), NumberStyles.Any, CultureInfo.InvariantCulture, out var usd)) info.Usd = usd; } catch (ApiException) { }
            return info;
        }

        public async Task<TransferSet> Transfers(string address, int max, bool asc, long fromTime, long toTime, CancellationToken ct)
        {
            var set = new TransferSet();
            string q = $"limit=200&only_confirmed=true&order_by=block_timestamp,{(asc ? "asc" : "desc")}" + (fromTime > 0 ? "&min_timestamp=" + fromTime * 1000 : "") + (toTime > 0 ? "&max_timestamp=" + toTime * 1000 : "");
            async Task Page(string path, Action<JsonElement> each)
            {
                string fp = null; int got = 0;
                while (got < max)
                {
                    var j = await Http.Json($"{H}/v1/accounts/{address}/{path}?{q}" + (fp != null ? "&fingerprint=" + fp : ""), Store.S.CacheSec, ct);
                    if (j == null) break;
                    int n = 0;
                    foreach (var e in j.Value.Arr("data")) { each(e); n++; }
                    got += n;
                    fp = j.Value.O("meta")?.S("fingerprint");
                    if (n == 0 || string.IsNullOrEmpty(fp)) return;
                }
                set.Truncated = true;
            }
            await Page("transactions/trc20", e =>
            {
                if (e.S("type") != "" && e.S("type") != "Transfer") return;
                var ti = e.O("token_info");
                int dec = (int)(ti?.L("decimals") ?? 6);
                decimal v = Units.From(e.S("value"), dec);
                if (v == 0) return;
                string sym = ti?.S("symbol") ?? "TOKEN"; if (sym == "") sym = "TOKEN"; if (sym.Length > 14) sym = sym.Substring(0, 14);
                set.Items.Add(new Xfer { Chain = C.Key, Hash = e.S("transaction_id"), From = e.S("from"), To = e.S("to"), Asset = sym, Contract = ti?.S("address") ?? "", Kind = "token", Amount = v, Time = e.L("block_timestamp") / 1000 });
            });
            await Page("transactions", e =>
            {
                var c = e.O("raw_data")?.Arr("contract").FirstOrDefault();
                if (c == null || c.Value.ValueKind != JsonValueKind.Object || c.Value.S("type") != "TransferContract") return;
                if (e.Arr("ret").FirstOrDefault().S("contractRet") is string r && r != "" && r != "SUCCESS") return;
                var v = c.Value.O("parameter")?.O("value"); if (v == null) return;
                decimal amt = v.Value.L("amount") / 1000000m; if (amt == 0) return;
                set.Items.Add(new Xfer { Chain = C.Key, Hash = e.S("txID"), From = Addr.TronFromHex(v.Value.S("owner_address")), To = Addr.TronFromHex(v.Value.S("to_address")), Asset = "TRX", Amount = amt, Time = e.L("block_timestamp") / 1000, Block = e.L("blockNumber") });
            });
            set.Items = (asc ? set.Items.OrderBy(x => x.Time) : set.Items.OrderByDescending(x => x.Time)).ToList();
            return set;
        }

        public async Task<List<Xfer>> Tx(string hash, CancellationToken ct)
        {
            var list = new List<Xfer>();
            var t = await Http.Json($"{H}/wallet/gettransactionbyid", Store.S.CacheSec, ct, "{\"value\":\"" + hash + "\"}");
            long time = 0;
            if (t != null && t.Value.O("raw_data") is JsonElement rd)
            {
                time = rd.L("timestamp") / 1000;
                var c = rd.Arr("contract").FirstOrDefault();
                if (c.ValueKind == JsonValueKind.Object && c.S("type") == "TransferContract" && c.O("parameter")?.O("value") is JsonElement v)
                    list.Add(new Xfer { Chain = C.Key, Hash = hash, From = Addr.TronFromHex(v.S("owner_address")), To = Addr.TronFromHex(v.S("to_address")), Asset = "TRX", Amount = v.L("amount") / 1000000m, Time = time });
            }
            var ev = await Http.Json($"{H}/v1/transactions/{hash}/events", Store.S.CacheSec, ct);
            if (ev != null)
                foreach (var e in ev.Value.Arr("data"))
                {
                    if (e.S("event_name") != "Transfer") continue;
                    var r = e.O("result"); if (r == null) continue;
                    string c = e.S("contract_address");
                    bool known = Chains.KnownTokens.TryGetValue(c, out var k);
                    decimal amt = Units.From(r.Value.S("value"), known ? k.dec : 6);
                    if (amt == 0) continue;
                    list.Add(new Xfer { Chain = C.Key, Hash = hash, From = Addr.TronFromHex(r.Value.S("from")), To = Addr.TronFromHex(r.Value.S("to")), Asset = known ? k.sym : "token " + Fmt.Short(c) + " (6 dec. assumed)", Contract = c, Kind = "token", Amount = amt, Time = e.L("block_timestamp") / 1000 });
                }
            if (list.Count == 0) throw new ApiException("Transaction not found on Tron (or it moved no value)");
            return list;
        }
    }
}
