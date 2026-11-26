using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Athar
{
    /// <summary>One value movement, normalised across chains.</summary>
    public class Xfer
    {
        public string Chain { get; set; }
        public string Hash { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string Asset { get; set; }
        public string Contract { get; set; } = "";
        public string Kind { get; set; } = "native";   // native | token | internal
        public decimal Amount { get; set; }
        public long Time { get; set; }                 // unix seconds, UTC
        public long Block { get; set; }
    }

    public class TransferSet
    {
        public List<Xfer> Items = new();
        public bool Truncated;
        public string Note = "";
    }

    public class AddrInfo
    {
        public string Chain { get; set; }
        public string Address { get; set; }
        public string Name { get; set; } = "";
        public List<string> Tags { get; set; } = new();
        public bool IsContract { get; set; }
        public bool IsScam { get; set; }
        public decimal Balance { get; set; }
        public long TxCount { get; set; } = -1;
        public decimal Usd { get; set; }
        public string Note { get; set; } = "";
    }

    public class ApiException : Exception { public ApiException(string m) : base(m) { } }

    public static class Fmt
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static string Short(string a)
        {
            if (string.IsNullOrEmpty(a)) return "—";
            int c = a.IndexOf(':');
            if (c > 0 && c < 12) return a.Substring(0, c + 1) + Short(a.Substring(c + 1));
            return a.Length <= 16 ? a : a.Substring(0, 8) + "…" + a.Substring(a.Length - 6);
        }
        public static string Amt(decimal v)
        {
            decimal a = Math.Abs(v);
            if (a == 0) return "0";
            if (a >= 1000) return v.ToString("#,##0.##", Inv);
            if (a >= 1) return v.ToString("0.####", Inv);
            if (a >= 0.0001m) return v.ToString("0.######", Inv);
            return v.ToString("0.########", Inv);
        }
        public static string Time(long unix) => unix <= 0 ? "—" : DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", Inv);
        public static string Day(long unix) => unix <= 0 ? "—" : DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.ToString("yyyy-MM-dd", Inv);
        public static string Dur(long sec)
        {
            sec = Math.Abs(sec);
            if (sec < 60) return sec + "s";
            if (sec < 3600) return (sec / 60) + "m " + (sec % 60) + "s";
            if (sec < 86400) return (sec / 3600) + "h " + (sec % 3600 / 60) + "m";
            return (sec / 86400) + "d " + (sec % 86400 / 3600) + "h";
        }
        public static long ParseDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            s = s.Trim();
            if (long.TryParse(s, out long u) && u > 1_000_000_000) return u;
            if (DateTime.TryParse(s, Inv, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)) return new DateTimeOffset(d, TimeSpan.Zero).ToUnixTimeSeconds();
            return 0;
        }
        public static string Html(string s) => WebUtility.HtmlEncode(s ?? "");
        public static string Csv(string s) { s ??= ""; return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s; }
        public static string Num(decimal v) => v.ToString(Inv);
    }

    public static class Units
    {
        /// <summary>Raw integer string with <paramref name="dec"/> decimals to decimal, without overflowing on huge values.</summary>
        public static decimal From(string raw, int dec)
        {
            if (string.IsNullOrEmpty(raw)) return 0;
            if (!BigInteger.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b)) return 0;
            if (dec < 0) dec = 0;
            if (dec > 10) { b /= BigInteger.Pow(10, dec - 10); dec = 10; }
            var cap = new BigInteger(decimal.MaxValue) / 1000;
            if (b > cap) b = cap;
            decimal d = (decimal)b;
            for (int i = 0; i < dec; i++) d /= 10m;
            return d;
        }
        public static decimal FromHex(string hex, int dec)
        {
            if (string.IsNullOrEmpty(hex)) return 0;
            if (hex.StartsWith("0x")) hex = hex.Substring(2);
            if (hex.Length == 0) return 0;
            return From(BigInteger.Parse("0" + hex, NumberStyles.HexNumber).ToString(), dec);
        }
        public static long HexLong(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return 0;
            if (hex.StartsWith("0x")) hex = hex.Substring(2);
            return hex.Length == 0 ? 0 : long.Parse(hex, NumberStyles.HexNumber);
        }
    }

    /// <summary>Separates real assets from spam: look-alike symbols, empty symbols, advertising tokens and impersonations of the coin or of major stablecoins.</summary>
    public static class Tokens
    {
        static readonly HashSet<string> Majors = new() { "USDT", "USDC", "DAI", "WETH", "WBTC", "BUSD", "TUSD", "USDD" };
        public static bool Suspicious(Xfer x)
        {
            if (x.Kind != "token") return false;
            string s = x.Asset ?? "";
            if (s == "" || s == "TOKEN") return true;
            if (s.StartsWith("token 0x") || s.StartsWith("token T")) return false;
            foreach (char c in s) if (c > 0x7E || c < 0x21) return true;
            string u = s.ToUpperInvariant();
            if (u.Contains("HTTP") || u.Contains("WWW") || u.Contains(".COM") || u.Contains(".IO") || u.Contains(".ORG") || u.Contains(".NET") || u.Contains("CLAIM") || u.Contains("VISIT") || u.Contains("REWARD") || u.Contains("AIRDROP")) return true;
            var chain = Chains.All.FirstOrDefault(c => c.Key == x.Chain);
            if (chain != null && u == chain.Symbol.ToUpperInvariant()) return true;
            if ((x.Chain == "eth" || x.Chain == "tron") && Chains.KnownTokens.Values.Any(k => k.sym == u) && !Chains.KnownTokens.ContainsKey(x.Contract ?? "")) return true;
            return false;
        }
        /// <summary>The coin itself and widely used tokens; the default set followed when tracing.</summary>
        public static bool Major(Xfer x)
        {
            if (x.Kind != "token") return true;
            if (Chains.KnownTokens.ContainsKey(x.Contract ?? "")) return true;
            return x.Chain != "eth" && x.Chain != "tron" && Majors.Contains((x.Asset ?? "").ToUpperInvariant());
        }
        public static List<Xfer> Clean(IEnumerable<Xfer> xs) => xs.Where(x => !Suspicious(x)).ToList();
    }

    public static class Addr
    {
        const string B58 = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        static bool IsHex(string s) => s.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
        static bool IsB58(string s) => s.All(c => B58.IndexOf(c) >= 0);

        /// <summary>Returns (kind, family): kind = address | tx | unknown; family = evm | btc | tron | btc-or-tron.</summary>
        public static (string kind, string family) Detect(string s)
        {
            s = (s ?? "").Trim();
            if (s.StartsWith("0x") || s.StartsWith("0X"))
            {
                string h = s.Substring(2);
                if (h.Length == 40 && IsHex(h)) return ("address", "evm");
                if (h.Length == 64 && IsHex(h)) return ("tx", "evm");
                return ("unknown", "");
            }
            if (s.Length == 64 && IsHex(s)) return ("tx", "btc-or-tron");
            if (s.Length == 34 && s[0] == 'T' && IsB58(s)) return ("address", "tron");
            if (s.StartsWith("bc1", StringComparison.OrdinalIgnoreCase) && s.Length >= 14 && s.Length <= 74) return ("address", "btc");
            if (s.Length >= 26 && s.Length <= 35 && (s[0] == '1' || s[0] == '3') && IsB58(s)) return ("address", "btc");
            return ("unknown", "");
        }

        public static string Norm(string family, string a)
        {
            a = (a ?? "").Trim();
            if (family == "evm") return a.ToLowerInvariant();
            if (family == "btc" && a.StartsWith("bc1", StringComparison.OrdinalIgnoreCase)) return a.ToLowerInvariant();
            return a;
        }

        /// <summary>Node ids of another network are written "network:address"; this returns the address part.</summary>
        public static string Bare(string id) { if (string.IsNullOrEmpty(id)) return id; int c = id.IndexOf(':'); return c > 0 && c < 12 ? id.Substring(c + 1) : id; }

        /// <summary>Tron base58 address to its 20-byte hex body (without the 41 prefix).</summary>
        public static string TronToHex(string b58)
        {
            BigInteger n = 0;
            foreach (char ch in b58) { int d = B58.IndexOf(ch); if (d < 0) return ""; n = n * 58 + d; }
            var bytes = n.ToByteArray(isUnsigned: true, isBigEndian: true);
            if (bytes.Length < 25) return "";
            return Convert.ToHexString(bytes, bytes.Length - 24, 20).ToLowerInvariant();
        }

        /// <summary>Tron hex address (41… or 0x… 20 bytes) to base58check.</summary>
        public static string TronFromHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return "";
            if (hex.StartsWith("0x")) hex = hex.Substring(2);
            if (hex.Length == 40) hex = "41" + hex;
            if (hex.Length != 42) return hex;
            byte[] raw = Convert.FromHexString(hex);
            byte[] chk = SHA256.HashData(SHA256.HashData(raw));
            byte[] all = raw.Concat(chk.Take(4)).ToArray();
            var n = new BigInteger(all, isUnsigned: true, isBigEndian: true);
            var sb = new StringBuilder();
            while (n > 0) { n = BigInteger.DivRem(n, 58, out var r); sb.Insert(0, B58[(int)r]); }
            foreach (byte b in all) { if (b == 0) sb.Insert(0, '1'); else break; }
            return sb.ToString();
        }
    }

    /// <summary>HTTP GET/POST with a disk cache, per-host pacing and retries.</summary>
    public static class Http
    {
        static readonly HttpClient C = Make();
        static HttpClient Make()
        {
            var h = new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, PooledConnectionLifetime = TimeSpan.FromMinutes(5), MaxConnectionsPerServer = 6 };
            var c = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(60) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Athar/1.0");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return c;
        }
        sealed class Gate { public readonly SemaphoreSlim Sem = new(1, 1); public DateTime Next = DateTime.MinValue; }
        static readonly ConcurrentDictionary<string, Gate> Gates = new();
        /// <summary>Extra header per host (for services that want an API key), e.g. api.relay.link → x-api-key.</summary>
        public static readonly ConcurrentDictionary<string, (string name, string value)> HostHeaders = new();
        public static int Calls, CacheHits;
        /// <summary>When true, cached replies are ignored (used by the watchlist).</summary>
        public static bool Fresh;
        public static readonly ConcurrentDictionary<string, DateTime> Sources = new();

        static int GapMs(string host)
        {
            int user = Store.S?.GapMs ?? 0;
            int g = host.Contains("routescan") ? 600 : host.Contains("etherscan") ? 380 : host.Contains("trongrid") ? 400 : host.Contains("relay.link") ? 1300 : 250;
            return Math.Max(g, user);
        }

        static string CachePath(string key)
        {
            string h = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).Substring(0, 40);
            return Path.Combine(Store.Cache, h + ".json");
        }

        public static async Task<string> Get(string url, int ttlSec, CancellationToken ct, string post = null, int tries = 5, int timeoutSec = 25)
        {
            string key = url + "\n" + post;
            string cp = Store.Cache != null ? CachePath(key) : null;
            if (cp != null && ttlSec > 0 && !Fresh && File.Exists(cp) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(cp)).TotalSeconds < ttlSec)
            {
                Interlocked.Increment(ref CacheHits);
                return await File.ReadAllTextAsync(cp, ct);
            }
            var uri = new Uri(url);
            var gate = Gates.GetOrAdd(uri.Host, _ => new Gate());
            Exception last = null;
            int timeouts = 0;
            for (int attempt = 0; attempt < tries; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                await gate.Sem.WaitAsync(ct);
                try
                {
                    var wait = gate.Next - DateTime.UtcNow;
                    if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
                    gate.Next = DateTime.UtcNow.AddMilliseconds(GapMs(uri.Host));
                }
                finally { gate.Sem.Release(); }
                try
                {
                    Interlocked.Increment(ref Calls);
                    using var req = new HttpRequestMessage(post == null ? HttpMethod.Get : HttpMethod.Post, uri);
                    if (post != null) req.Content = new StringContent(post, Encoding.UTF8, "application/json");
                    if (HostHeaders.TryGetValue(uri.Host, out var hh) && hh.value != "") req.Headers.TryAddWithoutValidation(hh.name, hh.value);
                    using var tcs = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    tcs.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
                    using var resp = await C.SendAsync(req, tcs.Token);
                    string body = await resp.Content.ReadAsStringAsync(tcs.Token);
                    int code = (int)resp.StatusCode;
                    if (code == 429 || code >= 500) { last = new ApiException($"{uri.Host}: HTTP {code}"); await Task.Delay(1200 * (attempt + 1), ct); continue; }
                    if (code == 404) return "";
                    if (code >= 400) throw new ApiException($"{uri.Host}: HTTP {code}" + (body.Contains("Just a moment") ? " (blocked by the site's bot check)" : ""));
                    Sources[uri.Host] = DateTime.UtcNow;
                    if (cp != null && body.Length > 0 && !body.Contains("rate limit", StringComparison.OrdinalIgnoreCase)) { try { await File.WriteAllTextAsync(cp, body, ct); } catch { } }
                    return body;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (OperationCanceledException) { last = new ApiException($"{uri.Host}: no answer within {timeoutSec} seconds"); if (++timeouts >= 2) break; }
                catch (ApiException) { throw; }
                catch (Exception e) { last = new ApiException($"{uri.Host}: {e.Message}"); await Task.Delay(900 * (attempt + 1), ct); }
            }
            throw last ?? new ApiException(uri.Host + ": request failed");
        }

        public static async Task<JsonElement?> Json(string url, int ttlSec, CancellationToken ct, string post = null, int tries = 5, int timeoutSec = 25)
        {
            string s = await Get(url, ttlSec, ct, post, tries, timeoutSec);
            if (string.IsNullOrWhiteSpace(s)) return null;
            try { using var d = JsonDocument.Parse(s); return d.RootElement.Clone(); }
            catch { throw new ApiException(new Uri(url).Host + ": unexpected (non-JSON) reply"); }
        }
    }

    public static class J
    {
        public static string S(this JsonElement e, string name)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return "";
            return v.ValueKind switch { JsonValueKind.String => v.GetString() ?? "", JsonValueKind.Number => v.GetRawText(), JsonValueKind.True => "true", JsonValueKind.False => "false", _ => "" };
        }
        public static long L(this JsonElement e, string name) { string s = S(e, name); return long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out long v) ? v : (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? (long)d : 0); }
        public static bool B(this JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
        public static JsonElement? O(this JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v : (JsonElement?)null;
        public static IEnumerable<JsonElement> Arr(this JsonElement? e) => e.HasValue && e.Value.ValueKind == JsonValueKind.Array ? e.Value.EnumerateArray() : Enumerable.Empty<JsonElement>();
        public static IEnumerable<JsonElement> Arr(this JsonElement e, string name) => Arr(O(e, name));
    }
}
