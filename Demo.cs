using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Athar
{
    /// <summary>Invented, offline scenario used by the demo, the self-tests and the screenshots. None of these addresses exist.</summary>
    public class Demo : IChainApi, IFreezeCheck
    {
        public Task<decimal> FrozenAmount(string address, List<string> tokens, CancellationToken ct) => Task.FromResult(address == FrozenWallet ? 800m : 0m);
        public Task<List<string>> Frozen(string address, CancellationToken ct) => Task.FromResult(address == FrozenWallet ? new List<string> { "USDT" } : new List<string>());
        static string A(char c) => "0x" + new string(c, 38) + "d0";
        public static readonly string Victim = A('1'), Hacker = A('2'), Hop1 = A('3'), Hop2 = A('4'), Hop3 = A('5'), Exchange = A('6'), Mixer = A('7'), Bridge = A('8'),
            Funder = A('9'), Hacker2 = A('a'), Payout = A('b'), Dust = A('c'), Other = A('e'), FrozenWallet = "0x" + new string('0', 37) + "fd0";
        public const long T0 = 1750000000;   // 2025-06-15 15:06:40 UTC

        public static readonly List<Xfer> Data = Build();
        static readonly Dictionary<string, (string name, string[] tags, bool contract)> Names = new()
        {
            [Exchange] = ("Demo Exchange: Hot Wallet", new[] { "Exchange" }, false),
            [Mixer] = ("Demo Mixer", new[] { "Mixer" }, true),
            [Bridge] = ("Demo Bridge", new[] { "Bridge" }, true),
            [Victim] = ("Victim treasury (demo)", new string[0], false),
        };

        static List<Xfer> Build()
        {
            var l = new List<Xfer>(); int n = 0;
            void X(string f, string t, decimal amt, long min, string asset = "ETH")
                => l.Add(new Xfer { Chain = "demo", Hash = "0x" + (++n).ToString("x4") + new string('f', 58) + "d0", From = f, To = t, Amount = amt, Asset = asset, Time = T0 + min * 60, Block = 1000 + min, Kind = asset == "ETH" ? "native" : "token", Contract = asset == "ETH" ? "" : A('f') });
            X(Funder, Hacker, 0.5m, -3000); X(Funder, Hacker2, 0.5m, -2990);
            X(Victim, Hacker, 1000m, 0);
            X(Hacker, Hop1, 0.01m, 20); X(Hacker, Hop1, 400m, 31);
            X(Hacker, Hop2, 350m, 45);
            X(Hacker, Mixer, 187.35m, 70);
            X(Hacker, Dust, 5m, 90);
            X(Hop1, Hop3, 399.6m, 43);
            X(Hop3, Exchange, 399.2m, 58);
            X(Hop2, Bridge, 349.5m, 66);
            X(Mixer, Payout, 187.11m, 250); X(Mixer, Other, 12m, 260); X(Other, Mixer, 12.2m, 100);
            X(Hacker2, Hop1, 20m, 500); X(Hop1, Hop3, 19.9m, 512);
            X(Hacker, Hop2, 1500m, 120, "USDT"); X(Hop2, Exchange, 1499m, 140, "USDT");
            X(Hacker, FrozenWallet, 800m, 125, "USDT");
            return l;
        }

        public Task<AddrInfo> Info(string a, CancellationToken ct)
        {
            a = a.ToLowerInvariant();
            var i = new AddrInfo { Chain = "demo", Address = a, Usd = 2500 };
            if (Names.TryGetValue(a, out var n)) { i.Name = n.name; i.Tags.AddRange(n.tags); i.IsContract = n.contract; }
            i.Balance = Data.Where(x => x.Asset == "ETH" && x.To == a).Sum(x => x.Amount) - Data.Where(x => x.Asset == "ETH" && x.From == a).Sum(x => x.Amount);
            if (i.Balance < 0) i.Balance = 0;
            i.TxCount = a == Exchange ? 250000 : Data.Count(x => x.From == a || x.To == a);
            return Task.FromResult(i);
        }

        public Task<TransferSet> Transfers(string a, int max, bool asc, long fromTime, long toTime, CancellationToken ct)
        {
            a = a.ToLowerInvariant();
            var q = Data.Where(x => (x.From == a || x.To == a) && (fromTime == 0 || x.Time >= fromTime) && (toTime == 0 || x.Time <= toTime));
            return Task.FromResult(new TransferSet { Items = (asc ? q.OrderBy(x => x.Time) : q.OrderByDescending(x => x.Time)).Take(max * 3).ToList() });
        }

        public Task<List<Xfer>> Tx(string hash, CancellationToken ct)
        {
            var l = Data.Where(x => x.Hash == hash).ToList();
            if (l.Count == 0) throw new ApiException("Transaction not found in the demo data");
            return Task.FromResult(l);
        }

        public static void Register()
        {
            if (Chains.All.Any(c => c.Key == "demo")) return;
            Chains.All.Add(new Chain { Key = "demo", Name = "Demo · offline", Symbol = "ETH", Family = "evm", Explorer = "", Make = _ => new Demo() });
        }
    }
}
