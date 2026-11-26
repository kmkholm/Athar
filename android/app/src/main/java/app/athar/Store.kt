package app.athar

import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.math.BigDecimal

class CaseItem(val chain: String, val kind: String, val value: String, var role: String = "", var note: String = "")
class CaseFile(val id: String, var name: String) {
    var notes = ""; var created = 0L
    val items = ArrayList<CaseItem>(); val traces = ArrayList<Graph>(); val findings = ArrayList<String>()
}
class WatchItem(val chain: String, val address: String, val note: String, var lastTime: Long)
class Alert(val x: Xfer, val watched: String)

fun Xfer.json(): JSONObject = JSONObject().put("chain", chain).put("hash", hash).put("from", from).put("to", to).put("asset", asset).put("amount", amount.toPlainString()).put("time", time).put("contract", contract).put("kind", kind)
fun xferOf(j: JSONObject) = Xfer(j.s("chain"), j.s("hash"), j.s("from"), j.s("to"), j.s("asset"), j.s("amount").toBigDecimalOrNull() ?: BigDecimal.ZERO, j.l("time"), j.s("contract"), j.s("kind").ifEmpty { "native" })

/** Everything the app remembers, in one JSON file inside the app's private folder. */
object Store {
    private var file: File? = null
    var lang = "ar"; var examiner = ""; var defaultChain = "eth"; var activeCase = ""; var watchOn = true; var claudeModel = "claude-opus-5-5"; var relayKey = ""
    val recent = ArrayList<String>(); val watch = ArrayList<WatchItem>(); val alerts = ArrayList<Alert>(); val cases = ArrayList<CaseFile>()

    @Synchronized fun init(dir: File) {
        if (file != null) return
        file = File(dir, "athar.json")
        Http.cacheDir = File(dir, "cache").apply { mkdirs() }
        try { Http.cacheDir!!.listFiles()?.filter { System.currentTimeMillis() - it.lastModified() > 7 * 86400000L }?.forEach { it.delete() } } catch (_: Exception) { }
        val j = try { JSONObject(file!!.readText()) } catch (_: Exception) { return }
        lang = j.s("lang").ifEmpty { "ar" }; examiner = j.s("examiner"); defaultChain = j.s("chain").ifEmpty { "eth" }; activeCase = j.s("active"); watchOn = j.optBoolean("watchOn", true); claudeModel = j.s("model").ifEmpty { "claude-opus-5-5" }; relayKey = j.s("relay"); Http.hostHeaders["api.relay.link"] = "x-api-key" to relayKey
        Chains.etherscanKey = j.s("key")
        j.optJSONArray("recent")?.let { a -> for (i in 0 until a.length()) recent += a.optString(i) }
        j.list("watch").forEach { watch += WatchItem(it.s("chain"), it.s("address"), it.s("note"), it.l("last")) }
        j.list("alerts").forEach { a -> a.o("x")?.let { alerts += Alert(xferOf(it), a.s("watched")) } }
        j.list("labels").forEach { Labels.user += UserLabel(it.s("address"), it.s("name"), it.s("cat").ifEmpty { "user" }, it.s("note")) }
        j.list("cases").forEach { c ->
            cases += CaseFile(c.s("id"), c.s("name")).apply {
                notes = c.s("notes"); created = c.l("created")
                c.list("items").forEach { items += CaseItem(it.s("chain"), it.s("kind"), it.s("value"), it.s("role"), it.s("note")) }
                c.list("traces").forEach { traces += Graph.of(it) }
                c.optJSONArray("findings")?.let { a -> for (i in 0 until a.length()) findings += a.optString(i) }
            }
        }
    }

    @Synchronized fun save() {
        val f = file ?: return
        val j = JSONObject().put("lang", lang).put("examiner", examiner).put("chain", defaultChain).put("active", activeCase).put("watchOn", watchOn).put("model", claudeModel).put("relay", relayKey).put("key", Chains.etherscanKey)
            .put("recent", JSONArray(recent))
            .put("watch", JSONArray(watch.map { JSONObject().put("chain", it.chain).put("address", it.address).put("note", it.note).put("last", it.lastTime) }))
            .put("alerts", JSONArray(alerts.take(200).map { JSONObject().put("x", it.x.json()).put("watched", it.watched) }))
            .put("labels", JSONArray(Labels.user.map { JSONObject().put("address", it.address).put("name", it.name).put("cat", it.cat).put("note", it.note) }))
            .put("cases", JSONArray(cases.map { c ->
                JSONObject().put("id", c.id).put("name", c.name).put("notes", c.notes).put("created", c.created)
                    .put("items", JSONArray(c.items.map { JSONObject().put("chain", it.chain).put("kind", it.kind).put("value", it.value).put("role", it.role).put("note", it.note) }))
                    .put("traces", JSONArray(c.traces.map { it.json() })).put("findings", JSONArray(c.findings))
            }))
        try { val tmp = File(f.path + ".tmp"); tmp.writeText(j.toString()); tmp.renameTo(f) } catch (_: Exception) { }
    }

    fun addRecent(chain: String, value: String) { val k = "$chain|$value"; recent.remove(k); recent.add(0, k); while (recent.size > 12) recent.removeAt(recent.size - 1); save() }
    fun newCase(name: String): CaseFile = CaseFile(System.currentTimeMillis().toString(36), name).also { it.created = System.currentTimeMillis() / 1000; cases.add(0, it); activeCase = it.id; save() }
    fun active(defaultName: String): CaseFile = cases.firstOrNull { it.id == activeCase } ?: cases.firstOrNull() ?: newCase(defaultName)

    /** Returns the number of new transfers found on watched addresses. */
    suspend fun checkWatch(): Int {
        var found = 0
        Http.fresh = true
        try {
            for (w in watch.toList()) try {
                val set = Chains.get(w.chain).api.transfers(w.address, 50, false, w.lastTime + 1, 0)
                for (x in set.items.filter { it.time > w.lastTime }.sortedBy { it.time }) { alerts.add(0, Alert(x, w.address)); found++ }
                if (set.items.isNotEmpty()) w.lastTime = maxOf(w.lastTime, set.items.maxOf { it.time })
            } catch (_: ApiException) { }
        } finally { Http.fresh = false }
        save()
        return found
    }
}

object GraphGeo {
    const val W = 200.0; const val H = 50.0
    class Curve(val ax: Double, val ay: Double, val c1x: Double, val c1y: Double, val c2x: Double, val c2y: Double, val bx: Double, val by: Double, val arrowRight: Boolean) {
        val mx get() = (ax + 3 * c1x + 3 * c2x + bx) / 8; val my get() = (ay + 3 * c1y + 3 * c2y + by) / 8
    }
    fun curve(s: GNode, t: GNode): Curve {
        val sy = s.y + H / 2; val ty = t.y + H / 2
        if (t.x > s.x + 1) { val m = (t.x - (s.x + W)) / 2; return Curve(s.x + W, sy, s.x + W + m, sy, t.x - m, ty, t.x, ty, true) }
        if (t.x < s.x - 1) { val m = (s.x - (t.x + W)) / 2; return Curve(s.x, sy, s.x - m, sy, t.x + W + m, ty, t.x + W, ty, false) }
        return Curve(s.x + W, sy, s.x + W + 60, sy, t.x + W + 60, ty, t.x + W, ty, false)
    }
    fun thick(e: GEdge, g: Graph): Double {
        val max = g.edges.filter { it.asset == e.asset && it.contract == e.contract }.maxOf { it.amount }
        return if (max.signum() <= 0) 1.4 else 1.4 + 4.2 * Math.sqrt(e.amount.toDouble() / max.toDouble())
    }
    fun title(n: GNode) = if (n.label.isNotEmpty()) (if (n.label.length > 26) n.label.take(25) + "…" else n.label) else Fmt.short(n.id)
    fun color(n: GNode) = if (n.depth == 0) "#1565C0" else catColor(n.cat)
    fun catColor(cat: String) = when (cat) { "exchange" -> "#2E7D32"; "mixer", "threat" -> "#C62828"; "bridge" -> "#6A1B9A"; "dex" -> "#E65100"; "user" -> "#1565C0"; "other" -> "#00796B"; else -> "#78909C" }

    fun svg(g: Graph): String {
        if (g.nodes.isEmpty()) return ""
        val w = g.nodes.maxOf { it.x } + W + 80; val h = g.nodes.maxOf { it.y } + H + 40
        fun f(d: Double) = String.format(java.util.Locale.US, "%.1f", d)
        val sb = StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' viewBox='-20 -20 ${f(w)} ${f(h)}' width='100%' style='max-width:${f(w)}px;direction:ltr' font-family='sans-serif'>")
        sb.append("<defs><marker id='ar' viewBox='0 0 10 10' refX='9' refY='5' markerWidth='7' markerHeight='7' orient='auto'><path d='M0,0 L10,5 L0,10 z' fill='#78909C'/></marker></defs>")
        for (e in g.edges) {
            val s = g.node(e.from) ?: continue; val t = g.node(e.to) ?: continue; val c = curve(s, t)
            sb.append("<path d='M${f(c.ax)},${f(c.ay)} C${f(c.c1x)},${f(c.c1y)} ${f(c.c2x)},${f(c.c2y)} ${f(c.bx)},${f(c.by)}' fill='none' stroke='#90A4AE' stroke-width='${f(minOf(4.0, thick(e, g)))}' marker-end='url(#ar)'/>")
            sb.append("<text x='${f(c.mx)}' y='${f(c.my - 3)}' font-size='10.5' text-anchor='middle' fill='#37474F' stroke='#fff' stroke-width='3' paint-order='stroke'>${Fmt.html(Fmt.amt(e.amount) + " " + e.asset)}</text>")
        }
        for (n in g.nodes) {
            val col = color(n)
            sb.append("<rect x='${f(n.x)}' y='${f(n.y)}' width='$W' height='$H' rx='5' fill='${if (n.depth == 0) "#E3EEFB" else "#fff"}' stroke='${if (n.cat.isEmpty() && n.depth > 0) "#C5CDD6" else col}'/>")
            sb.append("<rect x='${f(n.x)}' y='${f(n.y)}' width='5' height='$H' rx='2' fill='$col'/>")
            sb.append("<text x='${f(n.x + 14)}' y='${f(n.y + 21)}' font-size='12' font-weight='600' fill='#1F2933'>${Fmt.html(title(n))}</text>")
            sb.append("<text x='${f(n.x + 14)}' y='${f(n.y + 38)}' font-size='10.5' fill='#66788A' font-family='monospace'>${Fmt.html(Fmt.short(n.id))}</text>")
        }
        return sb.append("</svg>").toString()
    }
}

object Report {
    private const val CSS = "body{font-family:sans-serif;color:#1F2933;margin:24px auto;max-width:1100px;padding:0 16px;line-height:1.55}h1{font-size:22px;margin:0 0 4px}h2{font-size:16px;margin:26px 0 10px;padding-bottom:6px;border-bottom:2px solid #1565C0}" +
        ".meta{color:#66788A;font-size:13px}table{border-collapse:collapse;width:100%;font-size:12.5px;margin:6px 0 14px}th{background:#F4F6F8;text-align:start;padding:7px 9px;border-bottom:1px solid #C5CDD6}td{padding:6px 9px;border-bottom:1px solid #EDF0F3;vertical-align:top}" +
        ".c{font-family:monospace;direction:ltr;unicode-bidi:embed;word-break:break-all}.n{text-align:end;direction:ltr;white-space:nowrap}.box{border:1px solid #DDE2E8;border-radius:6px;padding:12px;background:#F9FAFB;overflow:auto}.note{background:#FFF4E0;border-radius:5px;padding:10px 14px;font-size:13px;margin:8px 0}"

    private fun page(title: String, body: String, rtl: Boolean, t: (String) -> String) =
        "<!doctype html><html lang='${if (rtl) "ar" else "en"}' dir='${if (rtl) "rtl" else "ltr"}'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>${Fmt.html(title)}</title><style>$CSS</style></head><body>$body" +
            "<p class='meta' style='margin-top:30px'>${Fmt.html(t("Generated by Athar"))} 1.0.0 · ${Fmt.time(System.currentTimeMillis() / 1000)} UTC · ${Fmt.html(t("Developed by Dr. Mohammed Tawfik"))}</p></body></html>"

    private fun addrCell(a: String): String { val l = Labels.find(a); return (if (l != null && l.name.isNotEmpty()) "<b>${Fmt.html(l.name)}</b><br>" else "") + "<span class='c'>${Fmt.html(a)}</span>" }

    private fun traceBody(g: Graph, t: (String) -> String): String {
        val sb = StringBuilder()
        sb.append("<p class='meta'>${Fmt.html(t("Network"))}: ${Fmt.html(Chains.get(g.chain).name)} · ${Fmt.html(t("Direction"))}: ${Fmt.html(t(if (g.forward) "Forward" else "Backward"))} · ${Fmt.html(t("Traced on"))}: ${Fmt.time(g.created)} UTC<br>${Fmt.html(t("Start address"))}: <span class='c'>${Fmt.html(g.start)}</span></p>")
        sb.append("<div class='box'>${GraphGeo.svg(g)}</div>")
        sb.append("<h3>${Fmt.html(t("All flows"))}</h3><table><tr><th>${Fmt.html(t("From"))}</th><th>${Fmt.html(t("To"))}</th><th>${Fmt.html(t("Amount"))}</th><th>${Fmt.html(t("Transfers"))}</th><th>${Fmt.html(t("First (UTC)"))}</th><th>${Fmt.html(t("Largest transaction"))}</th></tr>")
        for (e in g.edges.sortedWith(compareBy<GEdge> { g.node(it.from)?.depth ?: 0 }.thenByDescending { it.amount }))
            sb.append("<tr><td>${addrCell(e.from)}</td><td>${addrCell(e.to)}</td><td class='n'><b>${Fmt.amt(e.amount)} ${Fmt.html(e.asset)}</b></td><td class='n'>${e.count}</td><td class='n'>${Fmt.time(e.first)}</td><td class='c'>${Fmt.html(e.hashes.firstOrNull() ?: "")}</td></tr>")
        sb.append("</table>")
        return sb.toString()
    }

    private fun method(t: (String) -> String) = "<h2>${Fmt.html(t("Method and limits"))}</h2><p>${Fmt.html(t("Data comes from public blockchain explorers at the time of the trace. At every hop the largest transfers made after the funds arrived are followed; tracing stops at known services, contracts and very active wallets. Labels come from the explorer, a built-in list and the examiner's own labels."))}</p>" +
        "<div class='note'>${Fmt.html(t("A trace shows where value moved, not who owns the addresses. After the first hops an address may mix the traced funds with unrelated money. Confirm every key step on the explorer before relying on it."))}</div>"

    fun traceHtml(g: Graph, examiner: String, rtl: Boolean, t: (String) -> String): String {
        val title = t("Fund-flow trace report")
        return page(title, "<h1>${Fmt.html(title)}</h1>" + (if (examiner.isNotEmpty()) "<p class='meta'>${Fmt.html(t("Examiner"))}: ${Fmt.html(examiner)}</p>" else "") + traceBody(g, t) + method(t), rtl, t)
    }

    fun caseHtml(c: CaseFile, examiner: String, rtl: Boolean, t: (String) -> String): String {
        val sb = StringBuilder("<h1>${Fmt.html(c.name)}</h1><p class='meta'>${Fmt.html(t("Case report"))} · ${Fmt.html(t("Opened"))}: ${Fmt.day(c.created)}" + (if (examiner.isNotEmpty()) " · ${Fmt.html(t("Examiner"))}: ${Fmt.html(examiner)}" else "") + "</p>")
        if (c.notes.isNotBlank()) sb.append("<h2>${Fmt.html(t("Summary"))}</h2><p>${Fmt.html(c.notes).replace("\n", "<br>")}</p>")
        if (c.findings.isNotEmpty()) sb.append("<h2>${Fmt.html(t("Findings"))}</h2><ol>" + c.findings.joinToString("") { "<li>${Fmt.html(it)}</li>" } + "</ol>")
        if (c.items.isNotEmpty()) {
            sb.append("<h2>${Fmt.html(t("Addresses and transactions"))}</h2><table><tr><th>#</th><th>${Fmt.html(t("Network"))}</th><th>${Fmt.html(t("Item"))}</th><th>${Fmt.html(t("Role"))}</th><th>${Fmt.html(t("Note"))}</th></tr>")
            c.items.forEachIndexed { i, it -> sb.append("<tr><td>${i + 1}</td><td>${Fmt.html(Chains.all.firstOrNull { ch -> ch.key == it.chain }?.name ?: it.chain)}</td><td>${if (it.kind == "address") addrCell(it.value) else "<span class='c'>${Fmt.html(it.value)}</span>"}</td><td>${Fmt.html(it.role)}</td><td>${Fmt.html(it.note)}</td></tr>") }
            sb.append("</table>")
        }
        c.traces.forEachIndexed { i, g -> sb.append("<h2>${Fmt.html(t("Trace"))} ${i + 1}: ${Fmt.html(t(if (g.forward) "Where the money went" else "Where the money came from"))}</h2>").append(traceBody(g, t)) }
        sb.append(method(t))
        return page(c.name, sb.toString(), rtl, t)
    }
}
