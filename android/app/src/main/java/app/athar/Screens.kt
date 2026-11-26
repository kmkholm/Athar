package app.athar

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.widget.Toast
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.gestures.detectTransformGestures
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material.icons.filled.ContentCopy
import androidx.compose.material.icons.filled.Folder
import androidx.compose.material.icons.filled.Label
import androidx.compose.material.icons.filled.Notifications
import androidx.compose.material.icons.filled.OpenInNew
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.withTransform
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.drawText
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.text.style.TextDirection
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Constraints
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import java.math.BigDecimal
import java.math.RoundingMode

sealed interface Sc {
    data object Home : Sc
    data class Address(val chain: String, val addr: String) : Sc
    data class Tx(val chain: String, val hash: String) : Sc
    class Trace(val opt: TraceOptions? = null, val graph: Graph? = null) : Sc
    data object Tools : Sc
    data class Link(val preset: String = "") : Sc
    data object Match : Sc
    data object Watch : Sc
    data object More : Sc
    data object Cases : Sc
    data class CaseView(val id: String) : Sc
    data object LabelsSc : Sc
    data object Settings : Sc
}

object Nav {
    val stack = mutableStateListOf<Sc>(Sc.Home)
    fun go(s: Sc) { stack.add(s) }
    fun tab(s: Sc) { stack.clear(); stack.add(s) }
    fun back(): Boolean { if (stack.size <= 1) return false; stack.removeAt(stack.size - 1); return true }
    fun replace(s: Sc) { if (stack.isNotEmpty()) stack.removeAt(stack.size - 1); stack.add(s) }
}

fun toast(ctx: Context, text: String) = Toast.makeText(ctx, text, Toast.LENGTH_SHORT).show()
fun copy(ctx: Context, text: String) { (ctx.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager).setPrimaryClip(ClipData.newPlainText("athar", text)); toast(ctx, t("Copied")) }
fun openUrl(ctx: Context, url: String) { try { ctx.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url))) } catch (_: Exception) { } }

fun addToCase(ctx: Context, chain: String, kind: String, value: String, role: String = "") {
    val c = Store.active(t("Case") + " 1")
    if (c.items.any { it.value == value }) { toast(ctx, tf("Already in case “{0}”", c.name)); return }
    c.items += CaseItem(chain, kind, value, role); Store.save(); toast(ctx, tf("Added to case “{0}”", c.name))
}

/** Opens the right screen for whatever was pasted. Returns false when it is neither an address nor a hash. */
fun search(text0: String, sel: String): Boolean {
    val text = text0.trim()
    val (kind, fam) = Addr.detect(text)
    if (kind == "unknown") return false
    val sc = Chains.get(sel)
    val chain = when (fam) { "evm" -> if (sc.family == "evm") sel else "eth"; "btc" -> "btc"; "tron" -> "tron"; else -> if (sc.family == "tron") "tron" else if (sc.family == "btc") "btc" else "btc?" }
    Nav.go(if (kind == "address") Sc.Address(chain, Addr.norm(fam, text)) else Sc.Tx(chain, if (fam == "evm") text.lowercase() else text))
    return true
}

@Composable fun HomeScreen() {
    val ctx = LocalContext.current
    var q by remember { mutableStateOf("") }
    var chain by remember { mutableStateOf(Store.defaultChain) }
    Card(t("Follow the money on the blockchain"), t("Paste an address or a transaction hash (Ethereum, Bitcoin, Tron …)")) {
        Field(t("Address or transaction hash"), q, { q = it }, code = true, lines = 2)
        Wrap {
            ChainPicker(chain) { chain = it; Store.defaultChain = it; Store.save() }
            Column {
                Spacer(Modifier.height(16.dp))
                Row {
                    Primary(t("Search"), Icons.Filled.Search) { if (!search(q, chain)) toast(ctx, t("This is not a valid address or transaction hash.")) }
                    Plain(t("Paste")) { (ctx.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager).primaryClip?.getItemAt(0)?.text?.let { q = it.toString().trim() } }
                }
            }
        }
    }
    Notice("info", t("New here? Try the offline demo"), t("A small invented theft scenario (no internet needed) that shows every feature: test transaction, hops, mixer, bridge and exchange.")) { Nav.go(Sc.Address("demo", Demo.hacker)) }
    Card(t("Trace funds"), t("Follow the largest transfers forward or backward until they reach an exchange, a bridge or a mixer.")) { Plain(t("Open tracing")) { Nav.tab(Sc.Trace()) } }
    if (Store.recent.isNotEmpty()) Card(t("Recent searches")) {
        Store.recent.take(8).forEach { r ->
            val p = r.split('|'); if (p.size != 2 || Chains.all.none { it.key == p[0] }) return@forEach
            val isTx = Addr.detect(p[1]).first == "tx"
            Row(Modifier.fillMaxWidth().clickable { Nav.go(if (isTx) Sc.Tx(p[0], p[1]) else Sc.Address(p[0], p[1])) }.padding(vertical = 7.dp), verticalAlignment = Alignment.CenterVertically) {
                Chip(Chains.get(p[0]).name, C.Gray)
                Text(Labels.find(p[1])?.name?.takeIf { it.isNotEmpty() } ?: Fmt.short(p[1]), color = C.Brand, fontSize = 13.5.sp, style = TextStyle(textDirection = TextDirection.Ltr))
            }
        }
    }
    Card(t("Supported networks")) {
        Chains.all.forEach { c ->
            Row(Modifier.padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.padding(end = 8.dp).size(8.dp).clip(CircleShape).background(if (c.ready) C.Green else C.Amber))
                Text(c.name, fontWeight = FontWeight.SemiBold, fontSize = 13.5.sp, modifier = Modifier.width(120.dp))
                Text(if (c.key == "demo") t("Offline demo data") else if (c.ready) t("Ready — no key needed") else t("Needs a free Etherscan API key (Settings)"), fontSize = 12.5.sp, color = C.Muted)
            }
        }
    }
}

@Composable fun XferRow(x: Xfer, focus: String?, chain: String) {
    val out = focus != null && x.from == focus
    val other = if (focus == null) null else if (out) x.to else x.from
    Row(Modifier.fillMaxWidth().clickable { Nav.go(Sc.Tx(chain, x.hash)) }.padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
        if (focus != null) Text(if (out) t("OUT") else t("IN"), Modifier.width(46.dp), color = if (out) C.Red else C.Green, fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
        Column(Modifier.weight(1f)) {
            Text(if (other != null) Labels.show(other) else Labels.show(x.from) + " → " + Labels.show(x.to), fontSize = 13.5.sp, color = C.Text, style = TextStyle(textDirection = TextDirection.Ltr), maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(Fmt.time(x.time) + " UTC", fontSize = 11.5.sp, color = C.Muted, style = TextStyle(textDirection = TextDirection.Ltr))
        }
        Text(Fmt.amt(x.amount) + " " + x.asset, fontSize = 13.5.sp, fontWeight = FontWeight.SemiBold, color = C.Text, style = TextStyle(textDirection = TextDirection.Ltr), maxLines = 1)
    }
    HorizontalDivider(color = Color(0xFFEDF0F3))
}

@Composable fun LabelDialog(addr: String, onDone: () -> Unit) {
    val cur = Labels.user.firstOrNull { it.address.equals(addr, true) }
    var name by remember { mutableStateOf(cur?.name ?: "") }
    var cat by remember { mutableStateOf(cur?.cat ?: "user") }
    AlertDialog(onDismissRequest = onDone, title = { Text(t("Label this address")) }, text = {
        Column {
            Field(t("Name (e.g. Suspect wallet 1)"), name, { name = it })
            Picker(t("Type"), Labels.cats.map { it to catName(it) }, cat) { cat = it }
        }
    }, confirmButton = {
        TextButton({
            if (name.isNotBlank()) { Labels.user.removeAll { it.address.equals(addr, true) }; Labels.user += UserLabel(addr, name.trim(), cat); Store.save() }
            onDone()
        }) { Text(t("Save")) }
    }, dismissButton = { TextButton(onDone) { Text(t("Cancel")) } })
}

@Composable fun AddressScreen(s: Sc.Address) {
    val ctx = LocalContext.current
    val chain = remember(s.chain) { Chains.get(s.chain) }
    var labelling by remember { mutableStateOf(false) }
    var rev by remember { mutableIntStateOf(0) }
    if (labelling) LabelDialog(s.addr) { labelling = false; rev++ }
    Loader(s, { say ->
        val info = chain.api.info(s.addr); Labels.learn(info)
        val set = chain.api.transfers(s.addr, 2000, false)
        val cps = set.items.map { if (it.from == s.addr) it.to else it.from }.filter { it.isNotEmpty() && Labels.find(it) == null }.groupingBy { it }.eachCount().entries.sortedByDescending { it.value }.take(4).map { it.key }
        cps.forEachIndexed { i, cp -> say(tf("Reading labels of the main counterparties… {0}/{1}", i + 1, cps.size)); try { Labels.learn(chain.api.info(cp)) } catch (_: ApiException) { } }
        var first: Xfer? = null
        if (set.truncated) { say(t("Looking up the first funding…")); try { first = Analysis.firstFunder(s.addr, Tokens.clean(chain.api.transfers(s.addr, 10, true).items)) } catch (_: ApiException) { } }
        (chain.api as? FreezeCheck)?.let { fz -> try { val f = fz.frozen(s.addr); if (f.isNotEmpty()) info.note = "FROZEN:" + f.joinToString(", ") } catch (_: ApiException) { } }
        Store.addRecent(s.chain, s.addr)
        Triple(info, set, first)
    }) { (info, set, first) ->
        val xs = remember(set) { Tokens.clean(set.items) }
        rev.let { }
        Card {
            Code(s.addr, size = 14, bold = true)
            Spacer(Modifier.height(6.dp))
            Wrap { LabelChip(s.addr); if (info.isContract) Chip(t("Contract"), C.Gray); info.tags.take(4).forEach { Chip(it, C.Teal) } }
            if (info.note.startsWith("FROZEN:")) Notice("high", tf("Frozen by the issuer: {0}", info.note.substring(7)), t("The stablecoin issuer has blacklisted this address: its balance of that token cannot be moved."))
            else if (info.note.isNotEmpty()) Text(t(info.note), fontSize = 13.sp, color = C.Muted)
            Spacer(Modifier.height(8.dp))
            Wrap {
                Primary(t("Trace where the money went"), Icons.AutoMirrored.Filled.ArrowForward) { Nav.go(Sc.Trace(TraceOptions(s.chain, s.addr, true))) }
                Plain(t("Trace where it came from"), Icons.AutoMirrored.Filled.ArrowBack) { Nav.go(Sc.Trace(TraceOptions(s.chain, s.addr, false))) }
                Plain(t("Add to case"), Icons.Filled.Folder) { addToCase(ctx, s.chain, "address", s.addr) }
                Plain(t("Watch"), Icons.Filled.Notifications) {
                    if (s.chain == "demo") toast(ctx, t("The demo network cannot be watched."))
                    else if (Store.watch.any { it.address == s.addr && it.chain == s.chain }) toast(ctx, t("Already on the watchlist"))
                    else { Store.watch += WatchItem(s.chain, s.addr, "", System.currentTimeMillis() / 1000); Store.save(); WatchWorker.schedule(ctx); toast(ctx, t("Added to the watchlist")) }
                }
                Plain(t("Label"), Icons.Filled.Label) { labelling = true }
                Plain(t("Copy"), Icons.Filled.ContentCopy) { copy(ctx, s.addr) }
                if (s.chain != "demo") Plain(t("Open in explorer"), Icons.Filled.OpenInNew) { openUrl(ctx, chain.addrUrl(s.addr)) }
            }
        }
        Wrap {
            Stat(t("Balance"), Fmt.amt(info.balance) + " " + chain.symbol, if (info.usd.signum() > 0) "≈ $" + Fmt.amt((info.balance * info.usd).setScale(0, RoundingMode.HALF_UP)) else null)
            Stat(t("Transactions"), if (info.txCount >= 0) "%,d".format(java.util.Locale.US, info.txCount) else "—", tf("{0} transfers loaded", xs.size) + if (set.truncated) " *" else "")
            Stat(t("First seen"), if (xs.isNotEmpty()) Fmt.day(xs.minOf { it.time }) else "—")
            Stat(t("Last seen"), if (xs.isNotEmpty()) Fmt.day(xs.maxOf { it.time }) else "—")
        }
        Card(t("What stands out"), t("Automatic checks on the loaded transfers. Treat them as leads to verify, not as proof.")) {
            Analysis.findings(s.addr, info, set, first) { k, a -> tf(k, *a) }.forEach { f ->
                Notice(f.level, t(f.title), f.detail, if (f.address.isNotEmpty()) ({ Nav.go(Sc.Address(s.chain, f.address)) }) else if (f.hash.isNotEmpty()) ({ Nav.go(Sc.Tx(s.chain, f.hash)) }) else null)
            }
        }
        if (chain.family == "evm" && s.chain != "demo") Card(t("The same address on other networks"), t("An EVM address is valid on every EVM network; funds often continue there after a bridge.")) {
            Loader("net" + s.addr, { say ->
                Chains.all.filter { it.family == "evm" && it.key != s.chain && it.key != "demo" && it.ready }.map { c -> say(tf("Checking {0}…", c.name)); c to try { c.api.info(s.addr) } catch (_: ApiException) { null } }
            }) { res ->
                Column {
                    res.sortedBy { (_, i) -> if (i != null && (i.txCount > 0 || i.balance.signum() > 0)) 0 else 1 }.forEach { (c, i) ->
                        val active = i != null && (i.txCount > 0 || i.balance.signum() > 0)
                        Row(Modifier.fillMaxWidth().clickable { Nav.go(Sc.Address(c.key, s.addr)) }.padding(vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                            Box(Modifier.padding(end = 8.dp).size(8.dp).clip(CircleShape).background(if (active) C.Green else C.Line))
                            Text(c.name, Modifier.weight(1f), fontWeight = if (active) FontWeight.SemiBold else FontWeight.Normal, fontSize = 13.5.sp)
                            Text(if (i == null) t("Could not be read") else if (active) Fmt.amt(i.balance) + " " + c.symbol + (if (i.txCount > 0) " · " + i.txCount + " tx" else "") else t("No activity"), fontSize = 12.5.sp, color = if (active) C.Text else C.Muted, style = TextStyle(textDirection = TextDirection.Ltr))
                        }
                    }
                    val missing = Chains.all.filter { it.family == "evm" && it.needsKey && !it.ready }.map { it.name }
                    if (missing.isNotEmpty()) Text(tf("Not checked (need an Etherscan key in Settings): {0}", missing.joinToString(", ")), fontSize = 12.sp, color = C.Muted)
                }
            }
        }
        AiCard(t("AI analysis of this address")) { Ai.addressPrompt(chain, s.addr, info, set, first) }
        var dir by remember { mutableStateOf("all") }
        var all by remember { mutableStateOf(false) }
        Card(t("Transfers"), if (set.truncated) t("* Only the most recent transfers are loaded.") else null) {
            Picker(t("Direction"), listOf("all" to t("All directions"), "in" to t("Incoming only"), "out" to t("Outgoing only")), dir) { dir = it }
            val rows = xs.filter { dir == "all" || (dir == "in") == (it.to == s.addr) }
            if (rows.isEmpty()) Text(t("No transfers to show."), color = C.Muted, fontSize = 13.sp)
            rows.take(if (all) 300 else 25).forEach { XferRow(it, s.addr, s.chain) }
            if (rows.size > 25) TextButton({ all = !all }) { Text(if (all) t("Show less") else t("Show all") + " (${rows.size})") }
        }
        val cps = remember(xs, rev) { Analysis.counterparties(s.addr, xs) }
        if (cps.isNotEmpty()) Card(t("Counterparties"), t("Who this address deals with most. Double-click to open one.").substringBefore(". ") + ".") {
            cps.take(15).forEach { c ->
                Row(Modifier.fillMaxWidth().clickable { Nav.go(Sc.Address(s.chain, c.address)) }.padding(vertical = 7.dp), verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f)) {
                        Text(c.label.ifEmpty { Fmt.short(c.address) }, color = C.Brand, fontSize = 13.5.sp, fontWeight = FontWeight.SemiBold, style = TextStyle(textDirection = TextDirection.Ltr), maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Text((if (c.cat.isNotEmpty()) catName(c.cat) + " · " else "") + c.count + " tx · " + c.asset, fontSize = 11.5.sp, color = C.Muted)
                    }
                    Column(horizontalAlignment = Alignment.End) {
                        if (c.inn.signum() > 0) Text("+" + Fmt.amt(c.inn), color = C.Green, fontSize = 12.5.sp, style = TextStyle(textDirection = TextDirection.Ltr))
                        if (c.out.signum() > 0) Text("−" + Fmt.amt(c.out), color = C.Red, fontSize = 12.5.sp, style = TextStyle(textDirection = TextDirection.Ltr))
                    }
                }
                HorizontalDivider(color = Color(0xFFEDF0F3))
            }
        }
        val hours = remember(xs) { Analysis.hours(s.addr, xs) }
        if (hours.sum() >= 5) Card(t("Outgoing activity by hour (UTC)"), t("Quiet hours repeated every day hint at the operator's time zone or working hours.")) {
            val max = hours.max().coerceAtLeast(1)
            Ltr {
                Canvas(Modifier.fillMaxWidth().height(90.dp)) {
                    val bw = size.width / 24
                    for (h in 0 until 24) { val bh = size.height * hours[h] / max; drawRect(C.Brand, Offset(h * bw + bw * 0.15f, size.height - bh), Size(bw * 0.7f, bh)) }
                }
                Row(Modifier.fillMaxWidth()) { listOf("00", "06", "12", "18").forEach { Text(it, Modifier.weight(1f), fontSize = 10.5.sp, color = C.Muted) } }
            }
        }
    }
}

@Composable fun TxScreen(s: Sc.Tx) {
    val ctx = LocalContext.current
    Loader(s, { _ ->
        if (s.chain == "btc?") try { "btc" to Chains.get("btc").api.tx(s.hash) } catch (_: ApiException) { "tron" to Chains.get("tron").api.tx(s.hash) }
        else s.chain to Chains.get(s.chain).api.tx(s.hash)
    }) { (ck, list) ->
        val chain = Chains.get(ck)
        LaunchedEffect(s) { Store.addRecent(ck, s.hash) }
        Card {
            Code(s.hash, size = 13, bold = true)
            Text(chain.name + "  ·  " + Fmt.time(list.maxOf { it.time }) + " UTC", fontSize = 12.5.sp, color = C.Muted, modifier = Modifier.padding(vertical = 6.dp))
            Wrap {
                Plain(t("Add to case"), Icons.Filled.Folder) { addToCase(ctx, ck, "tx", s.hash) }
                Plain(t("Copy"), Icons.Filled.ContentCopy) { copy(ctx, s.hash) }
                if (ck != "demo") Plain(t("Open in explorer"), Icons.Filled.OpenInNew) { openUrl(ctx, chain.txUrl(s.hash)) }
            }
        }
        Card(t("Value moved in this transaction"), tf("{0} movement(s). Click an address to profile it.", list.size)) {
            list.forEach { x ->
                Column(Modifier.padding(vertical = 8.dp)) {
                    Text(Fmt.amt(x.amount) + " " + x.asset + if (x.kind == "internal") "  (" + t("internal") + ")" else "", fontWeight = FontWeight.SemiBold, fontSize = 15.sp, style = TextStyle(textDirection = TextDirection.Ltr))
                    @Composable fun party(label: String, a: String) = Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(label, Modifier.width(44.dp), fontSize = 12.5.sp, color = C.Muted)
                        if (a.isEmpty() || a == "coinbase" || a == "unknown") Text(if (a == "coinbase") t("Newly mined coins") else "—", fontSize = 13.sp, color = C.Muted)
                        else { Text(Labels.show(a), Modifier.clickable { Nav.go(Sc.Address(ck, a)) }.padding(vertical = 3.dp, horizontal = 2.dp), color = C.Brand, fontSize = 13.5.sp, style = TextStyle(textDirection = TextDirection.Ltr)); Spacer(Modifier.width(6.dp)); Labels.find(a)?.let { Chip(catName(it.cat), C.cat(it.cat)) } }
                    }
                    party(t("From"), x.from); party(t("To"), x.to)
                }
                HorizontalDivider(color = Color(0xFFEDF0F3))
            }
        }
        if (chain.family == "btc") Notice("info", t("Bitcoin note"), t("A Bitcoin transaction can have many inputs and outputs. The sender shown is the largest input; outputs back to an input address (change) are hidden."))
    }
}

// ------------------------------------------------------------------ trace

@Composable fun GraphView(g: Graph, version: Int, selected: GNode?, bubbles: Boolean, onSelect: (GNode?) -> Unit) {
    val tm = rememberTextMeasurer()
    val density = LocalDensity.current
    var scale by remember(g) { mutableFloatStateOf(0f) }
    var offset by remember(g) { mutableStateOf(Offset.Zero) }
    val W = GraphGeo.W.toFloat(); val H = GraphGeo.H.toFloat()
    val texts = remember(g, version, uiLang) {
        with(density) {
            val title = TextStyle(fontSize = 12.5f.toSp(), fontWeight = FontWeight.SemiBold, color = C.Text)
            val sub = TextStyle(fontSize = 10f.toSp(), color = C.Muted, fontFamily = FontFamily.Monospace)
            val lab = TextStyle(fontSize = 10.5f.toSp(), color = C.Text)
            val box = Constraints(maxWidth = (W - 20).toInt())
            Triple(
                g.nodes.associate { n -> n.id to (tm.measure(GraphGeo.title(n), title, maxLines = 1, overflow = TextOverflow.Ellipsis, constraints = box) to tm.measure(if (n.label.isNotEmpty()) Fmt.short(n.id) else if (n.depth == 0) "start" else Fmt.short(n.id), sub, maxLines = 1, constraints = box)) },
                g.edges.associateWith { e -> tm.measure(Fmt.amt(e.amount) + " " + e.asset, lab, maxLines = 1) }, 0)
        }
    }
    Ltr {
        BoxWithConstraints(Modifier.fillMaxWidth().height(300.dp).clip(RoundedCornerShape(6.dp)).background(C.Bg).border(1.dp, C.Line, RoundedCornerShape(6.dp))) {
            val wPx = constraints.maxWidth.toFloat(); val hPx = constraints.maxHeight.toFloat()
            LaunchedEffect(g, version, wPx, bubbles) {
                if (bubbles) {
                    val x0 = g.nodes.minOf { it.bx - it.r }.toFloat(); val y0 = g.nodes.minOf { it.by - it.r }.toFloat()
                    val bw = (g.nodes.maxOf { it.bx + it.r } - x0).toFloat().coerceAtLeast(1f); val bh = (g.nodes.maxOf { it.by + it.r } - y0).toFloat().coerceAtLeast(1f)
                    val s = minOf((wPx - 24) / bw, (hPx - 24) / bh, density.density * 1.1f)
                    scale = s; offset = Offset((wPx - bw * s) / 2 - x0 * s, (hPx - bh * s) / 2 - y0 * s)
                } else {
                    val gw = (g.nodes.maxOf { it.x } + GraphGeo.W + 70).toFloat(); val gh = (g.nodes.maxOf { it.y } + GraphGeo.H).toFloat()
                    val s = minOf((wPx - 24) / gw, (hPx - 24) / gh, density.density * 1.1f).coerceAtLeast(density.density * 0.22f)
                    scale = s; offset = Offset(((wPx - gw * s) / 2 + 6).coerceAtLeast(8f), ((hPx - gh * s) / 2).coerceAtLeast(8f))
                }
            }
            Canvas(Modifier.fillMaxSize()
                .pointerInput(g) { detectTransformGestures { c, pan, zoom, _ -> val ns = (scale * zoom).coerceIn(density.density * 0.03f, density.density * 3f); offset = c - (c - offset) * (ns / scale) + pan; scale = ns } }
                .pointerInput(g, version, bubbles) { detectTapGestures { p -> val x = (p.x - offset.x) / scale; val y = (p.y - offset.y) / scale; onSelect(if (bubbles) g.nodes.sortedBy { it.r }.firstOrNull { (x - it.bx) * (x - it.bx) + (y - it.by) * (y - it.by) <= it.r * it.r } else g.nodes.lastOrNull { x >= it.x && x <= it.x + W && y >= it.y && y <= it.y + H }) } }) {
                if (scale <= 0f) return@Canvas
                withTransform({ translate(offset.x, offset.y); scale(scale, scale, Offset.Zero) }) {
                    if (bubbles) {
                        for (e in g.edges) { val s0 = g.node(e.from) ?: continue; val t0 = g.node(e.to) ?: continue; drawLine(if (e.bridge) C.Purple.copy(alpha = 0.8f) else C.Gray.copy(alpha = 0.4f), Offset(s0.bx.toFloat(), s0.by.toFloat()), Offset(t0.bx.toFloat(), t0.by.toFloat()), minOf(6f, GraphGeo.thick(e, g).toFloat()), pathEffect = if (e.bridge) androidx.compose.ui.graphics.PathEffect.dashPathEffect(floatArrayOf(8f, 6f)) else null) }
                        for (n in g.nodes.sortedByDescending { it.r }) {
                            val col = statusColor(g.status(n)); val c0 = Offset(n.bx.toFloat(), n.by.toFloat())
                            drawCircle(Color.White, n.r.toFloat(), c0); drawCircle(col.copy(alpha = 0.22f), n.r.toFloat(), c0)
                            drawCircle(col, n.r.toFloat(), c0, style = Stroke(if (n === selected) 5f else 2f))
                            if (n.r >= 26) texts.first[n.id]?.let { (a, _) -> if (a.size.width <= n.r * 2 - 6) drawText(a, topLeft = Offset(c0.x - a.size.width / 2f, c0.y - a.size.height / 2f)) }
                        }
                        return@withTransform
                    }
                    for (e in g.edges) {
                        val s = g.node(e.from) ?: continue; val t = g.node(e.to) ?: continue; val c = GraphGeo.curve(s, t)
                        val path = Path().apply { moveTo(c.ax.toFloat(), c.ay.toFloat()); cubicTo(c.c1x.toFloat(), c.c1y.toFloat(), c.c2x.toFloat(), c.c2y.toFloat(), c.bx.toFloat(), c.by.toFloat()) }
                        drawPath(path, if (e.bridge) C.Purple.copy(alpha = 0.85f) else C.Gray.copy(alpha = 0.75f), style = Stroke(GraphGeo.thick(e, g).toFloat(), pathEffect = if (e.bridge) androidx.compose.ui.graphics.PathEffect.dashPathEffect(floatArrayOf(8f, 6f)) else null))
                        val d = if (c.arrowRight) -1f else 1f; val bx = c.bx.toFloat(); val by = c.by.toFloat()
                        drawPath(Path().apply { moveTo(bx, by); lineTo(bx + d * 9, by - 5); lineTo(bx + d * 9, by + 5); close() }, C.Gray)
                    }
                    for (e in g.edges) {
                        val s = g.node(e.from) ?: continue; val t = g.node(e.to) ?: continue; val c = GraphGeo.curve(s, t); val tl = texts.second[e] ?: continue
                        val lx = c.mx.toFloat() - tl.size.width / 2f; val ly = c.my.toFloat() - tl.size.height / 2f
                        drawRoundRect(Color.White, Offset(lx - 4, ly - 1), Size(tl.size.width + 8f, tl.size.height + 2f), CornerRadius(3f))
                        drawText(tl, topLeft = Offset(lx, ly))
                    }
                    for (n in g.nodes) {
                        val start = n.depth == 0; val col = statusColor(g.status(n)); val x = n.x.toFloat(); val y = n.y.toFloat()
                        drawRoundRect(if (start) C.BrandSoft else Color.White, Offset(x, y), Size(W, H), CornerRadius(4f))
                        drawRoundRect(col, Offset(x, y), Size(W, H), CornerRadius(4f), style = Stroke(if (n === selected) 3.5f else 1.2f))
                        drawRect(col, Offset(x, y), Size(5f, H))
                        texts.first[n.id]?.let { (a, b) -> drawText(a, topLeft = Offset(x + 13, y + 7)); drawText(b, topLeft = Offset(x + 13, y + 29)) }
                    }
                }
            }
        }
    }
}

@Composable fun TraceScreen(s: Sc.Trace) {
    val ctx = LocalContext.current
    val init = s.opt ?: TraceOptions(chain = s.graph?.chain ?: Store.defaultChain, start = s.graph?.start ?: "", forward = s.graph?.forward ?: true)
    var addr by remember(s) { mutableStateOf(init.start) }
    var chain by remember(s) { mutableStateOf(init.chain) }
    var forward by remember(s) { mutableStateOf(init.forward) }
    var hops by remember(s) { mutableStateOf("3") }
    var branch by remember(s) { mutableStateOf("25") }
    var share by remember(s) { mutableStateOf("3") }
    var from by remember(s) { mutableStateOf("") }
    var asset by remember(s) { mutableStateOf("") }
    var adv by remember(s) { mutableStateOf(false) }
    var allTok by remember(s) { mutableStateOf(false) }
    var bridgesOn by remember(s) { mutableStateOf(true) }
    var graph by remember(s) { mutableStateOf(s.graph) }
    var job by remember(s) { mutableStateOf<Pair<Int, suspend ((String) -> Unit) -> Graph>?>(null) }
    var version by remember(s) { mutableIntStateOf(0) }
    var selected by remember(s) { mutableStateOf<GNode?>(null) }
    fun opts() = TraceOptions(chain, addr.trim(), forward, hops.toInt(), 120, branch.toInt(), share.toDouble(), Fmt.parseDate(from), asset.trim(), allTokens = allTok, followBridges = bridgesOn)
    fun start() {
        if (Tracer.starts(Chains.get(chain), addr).isEmpty()) { toast(ctx, t("Enter a valid address for the selected network.")); return }
        val o = opts(); graph = null; selected = null
        job = (job?.first ?: 0) + 1 to { say -> Tracer.run(o) { say(t("Tracing…") + "  " + it) } }
    }
    LaunchedEffect(s) { if (s.opt != null && s.opt.start.isNotEmpty() && graph == null && job == null) start() }

    Card(t("Trace funds"), t("Start from an address. At every hop Athar keeps the largest transfers made after the money arrived, and stops when it reaches a known service (exchange, bridge, mixer) or a contract.")) {
        Field(t("Start address"), addr, { addr = it }, code = true, lines = 2, hint = t("Several addresses of the same incident can be given, separated by commas."))
        Wrap {
            ChainPicker(chain) { chain = it }
            Picker(t("Direction"), listOf("f" to t("Forward — where did the money go?"), "b" to t("Backward — where did it come from?")), if (forward) "f" else "b") { forward = it == "f" }
        }
        TextButton({ adv = !adv }) { Text(t("Advanced options") + if (adv) "  ▴" else "  ▾") }
        if (adv) {
            Wrap {
                Picker(t("Hops"), (1..5).map { "$it" to "$it" }, hops) { hops = it }
                Picker(t("Branches per address"), listOf("3", "5", "8", "15", "25", "40").map { it to it }, branch) { branch = it }
                Picker(t("Ignore below"), listOf("0.5", "1", "3", "5", "10", "20").map { it to "$it %" }, share) { share = it }
            }
            Field(t("Only after (UTC date)"), from, { from = it }, code = true, hint = t("e.g. 2025-02-21 or 2025-02-21 14:00"))
            Field(t("Only this asset"), asset, { asset = it }, code = true, hint = t("e.g. ETH, USDT — empty = all"))
            Row(verticalAlignment = Alignment.CenterVertically) { androidx.compose.material3.Switch(allTok, { allTok = it }); Text("  " + t("Follow every token, not only the coin and major stablecoins"), fontSize = 13.sp) }
            Row(verticalAlignment = Alignment.CenterVertically) { androidx.compose.material3.Switch(bridgesOn, { bridgesOn = it }); Text("  " + t("Follow bridges and cross-chain swaps to the other network"), fontSize = 13.sp) }
        }
        Primary(t("Start tracing"), Icons.Filled.PlayArrow) { start() }
    }
    val j = job
    if (graph == null && j != null) Loader(j.first, j.second) { g -> LaunchedEffect(g) { graph = g; version++ } }
    val g = graph
    if (g == null && j == null) Card { Steps("Paste the address that received the stolen or suspicious funds.", "Choose Forward to see where the money went, or Backward to find who funded it.", "Press Start. Click any box in the diagram to open it or to continue from it.", "Save the diagram to a case and export a report.") }
    if (g != null) TraceResult(g, version, selected, { selected = it }, opts()) { node ->
        val o = opts(); graph = null; selected = null
        job = (job?.first ?: 0) + 1 to { say -> Tracer.more(g, node, o, 2) { say(t("Tracing…") + "  " + it) }; g }
    }
}

@Composable fun TraceResult(g: Graph, version: Int, selected: GNode?, onSelect: (GNode?) -> Unit, opt: TraceOptions, onMore: (GNode) -> Unit) {
    val ctx = LocalContext.current
    val ends = g.nodes.filter { it.depth > 0 && it.stop.isNotEmpty() }
    Wrap {
        Stat(t("Addresses"), "${g.nodes.size}"); Stat(t("Flows"), "${g.edges.size}")
        Stat(t("Reached known services"), "${ends.count { it.stop == "service" }}", color = C.Green); Stat(t("Deepest hop"), "${g.nodes.maxOf { it.depth }}")
    }
    val nolabel = g.notes.count { it == "nolabel" }
    if (nolabel > 0) Notice("info", tf("The explorer was slow: labels of {0} address(es) could not be read. Run the trace again later to complete them.", nolabel))
    val endsNow = g.nodes.filter { it.depth > 0 || it.frozen.isNotEmpty() }
    Card(t("Where the money is now"), t("Totals of the traced value by where it stands, at today's price. “Still holding” wallets have not moved what they received.")) {
        Wrap {
            listOf("frozen", "holding", "mixer", "exchange", "bridge", "otherchain", "dex", "unexplored").forEach { st ->
                val ns = endsNow.filter { g.status(it) == st }; if (ns.isEmpty()) return@forEach
                val usd = if (st == "frozen") ns.map { it.frozenUsd }.total() else ns.map { n -> if (st == "holding" || Labels.isService(n.cat)) g.usdIn(n) else (g.usdIn(n) - g.usdOut(n)).max(BigDecimal.ZERO) }.total()
                Stat(statusName(st), if (usd.signum() > 0) money(usd) else "${ns.size}", tf("{0} address(es)", ns.size), statusColor(st))
            }
        }
    }
    var bubbles by remember(g) { mutableStateOf(false) }
    if ("relaykey" in g.notes) Notice("medium", t("Funds entered the Relay bridge. Its public lookup now requires an API key: add a Relay key in Settings to see where they came out.")) { Nav.go(Sc.Settings) }
    g.notes.filter { it != "nolabel" && it != "relaykey" }.distinct().take(4).forEach { Notice(if (it == "recent") "info" else "medium", if (it == "limit") t("The address limit was reached; some branches were not followed. Raise “Max addresses” or continue from a box.") else if (it == "recent") t("The start address is very active and no start date was given, so only its most recent transfers were used. Set “Only after” to the date of the incident for a precise trace.") else it) }
    Card(if (g.forward) t("Where the money went") else t("Where the money came from"), t("Pinch to zoom, drag to move.") + " " + t("Tap an address box to see its details.")) {
        Row(Modifier.padding(bottom = 8.dp)) {
            if (bubbles) Plain(t("Flow diagram")) { bubbles = false } else Primary(t("Flow diagram")) { }
            if (bubbles) Primary(t("Bubble map")) { } else Plain(t("Bubble map")) { bubbles = true }
        }
        GraphView(g, version, selected, bubbles, onSelect)
        Spacer(Modifier.height(8.dp))
        Wrap { listOf("start", "frozen", "holding", "exchange", "bridge", "mixer", "dex", "passed", "unexplored").forEach { Chip(statusName(it), statusColor(it)) } }
        val n = selected
        if (n != null) {
            HorizontalDivider(Modifier.padding(vertical = 8.dp), color = C.Line)
            Code(n.id, size = 12, bold = true)
            Wrap { LabelChip(n.id); Chip(statusName(g.status(n)) + if (n.frozen.isNotEmpty()) " · " + n.frozen else "", statusColor(g.status(n))); if (n.stop.isNotEmpty() && n.stop != "service" && n.stop != "end") Chip(stopName(n.stop), C.Gray) }
            val inn = g.edges.filter { it.to == n.id }.groupBy { it.asset }.map { Fmt.amt(it.value.map { e -> e.amount }.total()) + " " + it.key }
            val out = g.edges.filter { it.from == n.id }.groupBy { it.asset }.map { Fmt.amt(it.value.map { e -> e.amount }.total()) + " " + it.key }
            Text(t("In (traced)") + ": " + inn.joinToString(", ").ifEmpty { "—" } + "\n" + t("Out (traced)") + ": " + out.joinToString(", ").ifEmpty { "—" }, fontSize = 12.5.sp, color = C.Muted, lineHeight = 19.sp)
            Spacer(Modifier.height(6.dp))
            Wrap {
                Plain(t("Open address profile")) { Nav.go(Sc.Address(g.chainOf(n.id), Addr.bare(n.id))) }
                if (!n.expanded) Primary(t("Continue tracing from here (+2 hops)"), Icons.Filled.PlayArrow) { onMore(n) }
                Plain(t("Add to case"), Icons.Filled.Folder) { addToCase(ctx, g.chainOf(n.id), "address", Addr.bare(n.id)) }
                Plain(t("Copy"), Icons.Filled.ContentCopy) { copy(ctx, Addr.bare(n.id)) }
            }
        }
        Spacer(Modifier.height(8.dp))
        Wrap {
            Plain(t("Save to case"), Icons.Filled.Folder) {
                val c = Store.active(t("Case") + " 1")
                if (g !in c.traces) c.traces += g
                g.nodes.filter { it.depth == 0 }.forEach { st -> if (c.items.none { it.value == st.id }) c.items += CaseItem(g.chain, "address", st.id, "trace start") }
                Store.save(); toast(ctx, tf("Added to case “{0}”", c.name))
            }
            SaveReport(t("Save report (HTML)"), "athar-trace.html") { Report.traceHtml(g, Store.examiner, uiLang == "ar") { t(it) } }
        }
    }
    AiCard(t("AI explanation of this trace")) { Ai.tracePrompt(g) }
    if (ends.isNotEmpty()) Card(t("Where the trail ends"), t("Funds that reach an exchange can be frozen or identified by that exchange after a formal request; note the address and the transaction.")) {
        ends.sortedByDescending { g.usdIn(it) }.take(60).forEach { n ->
            Row(Modifier.fillMaxWidth().clickable { Nav.go(Sc.Address(g.chainOf(n.id), Addr.bare(n.id))) }.padding(vertical = 7.dp), verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.padding(end = 8.dp).size(9.dp).clip(CircleShape).background(statusColor(g.status(n))))
                Column(Modifier.weight(1f)) {
                    Text(n.label.ifEmpty { Fmt.short(n.id) }, fontWeight = FontWeight.SemiBold, fontSize = 13.5.sp, style = TextStyle(textDirection = TextDirection.Ltr), maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Text(statusName(g.status(n)) + " · " + t("Hop") + " " + n.depth, fontSize = 11.5.sp, color = C.Muted)
                }
                Text(g.edges.filter { (if (g.forward) it.to else it.from) == n.id }.groupBy { it.asset }.map { Fmt.amt(it.value.map { e -> e.amount }.total()) + " " + it.key }.joinToString("\n"), fontSize = 12.5.sp, fontWeight = FontWeight.SemiBold, style = TextStyle(textDirection = TextDirection.Ltr))
            }
            HorizontalDivider(color = Color(0xFFEDF0F3))
        }
    }
    Card(t("All flows")) {
        g.edges.sortedWith(compareBy<GEdge> { g.node(it.from)?.depth ?: 0 }.thenByDescending { it.amount }).forEach { e ->
            Row(Modifier.fillMaxWidth().clickable { e.hashes.firstOrNull()?.let { Nav.go(Sc.Tx(e.chainKey.ifEmpty { g.chain }, it)) } }.padding(vertical = 7.dp), verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text(Labels.show(e.from) + " → " + Labels.show(e.to), fontSize = 13.sp, style = TextStyle(textDirection = TextDirection.Ltr), maxLines = 1, overflow = TextOverflow.Ellipsis)
                    Text(Fmt.time(e.first) + " · " + e.count + " tx" + (g.usd(e)?.let { " · ≈ $" + Fmt.amt(it.setScale(0, RoundingMode.HALF_UP)) } ?: ""), fontSize = 11.5.sp, color = C.Muted, style = TextStyle(textDirection = TextDirection.Ltr))
                }
                Text(Fmt.amt(e.amount) + " " + e.asset, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, style = TextStyle(textDirection = TextDirection.Ltr))
            }
            HorizontalDivider(color = Color(0xFFEDF0F3))
        }
    }
    Notice("info", t("How to read a trace"), t("A trace shows where value moved, not who owns the addresses. After the first hops an address may mix the traced funds with unrelated money. Confirm every key step on the explorer before relying on it."))
}
