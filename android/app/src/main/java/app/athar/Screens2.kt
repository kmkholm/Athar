package app.athar

import android.Manifest
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.CompareArrows
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.AutoAwesome
import androidx.compose.material.icons.filled.ContentCopy
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Folder
import androidx.compose.material.icons.filled.Label
import androidx.compose.material.icons.filled.Link
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Save
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDirection
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.launch

/** Lets the user choose where to save an HTML report (no storage permission needed). */
@Composable fun SaveReport(label: String, fileName: String, html: () -> String) {
    val ctx = LocalContext.current
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("text/html")) { uri ->
        if (uri != null) try { ctx.contentResolver.openOutputStream(uri)?.use { it.write(html().toByteArray()) }; toast(ctx, t("Report saved")) } catch (e: Exception) { toast(ctx, e.message ?: "error") }
    }
    Plain(label, Icons.Filled.Save) { launcher.launch(fileName) }
}

/** Optional AI analysis card: asks Claude with the user's own key and shows the answer in place. */
@Composable fun AiCard(title: String, useLabel: String? = null, onUse: ((String) -> Unit)? = null, prompt: () -> String) {
    val ctx = LocalContext.current
    val key = remember { SecureKey.load(ctx) }
    var run by remember { mutableIntStateOf(0) }
    Card(title, if (key.isBlank()) null else t("Sends the public blockchain data shown on this page to the Claude API with your own key.")) {
        if (key.isBlank()) { Notice("info", t("AI analysis is optional"), t("Add your Claude API key in Settings to use AI analysis.")) { Nav.go(Sc.Settings) }; return@Card }
        Primary(t("Analyse with AI"), Icons.Filled.AutoAwesome) { run++ }
        if (run > 0) {
            Spacer(Modifier.height(10.dp))
            Loader(run, { say -> say(t("Asking Claude…")); Ai.ask(key, Store.claudeModel, prompt(), uiLang == "ar") }) { text ->
                Column {
                    SelectionContainer { Text(text, Modifier.fillMaxWidth().clip(RoundedCornerShape(6.dp)).background(C.Bg).padding(12.dp), fontSize = 14.sp, lineHeight = 22.sp, color = C.Text) }
                    Spacer(Modifier.height(8.dp))
                    Wrap { Plain(t("Copy"), Icons.Filled.ContentCopy) { copy(ctx, text) }; if (onUse != null && useLabel != null) Plain(useLabel, Icons.Filled.Folder) { onUse(text) } }
                    Text(t("Written by an AI model from the data above. Check every statement against the explorer before relying on it."), fontSize = 12.sp, color = C.Muted)
                }
            }
        }
    }
}

@Composable fun MenuRow(icon: ImageVector, title: String, sub: String, onClick: () -> Unit) {
    Card(modifier = Modifier.clickable(onClick = onClick)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(icon, null, Modifier.padding(end = 14.dp).size(26.dp), tint = C.Brand)
            Column(Modifier.weight(1f)) { Text(title, fontSize = 15.5.sp, fontWeight = FontWeight.SemiBold, color = C.Text); Text(sub, fontSize = 12.5.sp, color = C.Muted, lineHeight = 18.sp) }
        }
    }
}

@Composable fun ToolsScreen() {
    MenuRow(Icons.Filled.Link, t("Link addresses"), t("Find direct transfers, shared counterparties and a common funder between several wallets.")) { Nav.go(Sc.Link()) }
    MenuRow(Icons.AutoMirrored.Filled.CompareArrows, t("Match deposits and withdrawals"), t("Pair what went into a bridge or privacy tool with what came out, by amount and timing.")) { Nav.go(Sc.Match) }
}

@Composable fun MoreScreen() {
    MenuRow(Icons.Filled.Folder, t("Cases & reports"), t("Collect addresses, traces and notes in a case and export a printable report.")) { Nav.go(Sc.Cases) }
    MenuRow(Icons.Filled.Label, t("Labels"), t("Names make a trace readable. Your own labels win over the explorer's tags and the small built-in list.")) { Nav.go(Sc.LabelsSc) }
    MenuRow(Icons.Filled.Settings, t("Settings"), t("Language") + " · Claude API · Etherscan") { Nav.go(Sc.Settings) }
}

@Composable fun LinkScreen(s: Sc.Link) {
    val ctx = LocalContext.current
    var text by remember { mutableStateOf(s.preset) }
    var chain by remember { mutableStateOf(Store.defaultChain) }
    var skip by remember { mutableStateOf(true) }
    var job by remember { mutableStateOf<Triple<Int, Chain, List<String>>?>(null) }
    Card(t("Link addresses"), t("Give two or more addresses. Athar looks for direct transfers between them, counterparties they share and a common first funder — the usual signs that wallets belong to the same operator.")) {
        Field(t("Addresses (one per line)"), text, { text = it }, code = true, lines = 4)
        ChainPicker(chain) { chain = it }
        Row(verticalAlignment = Alignment.CenterVertically) { Switch(skip, { skip = it }); Text("  " + t("Ignore shared exchanges, bridges and contracts (everyone uses them)"), fontSize = 13.sp) }
        Spacer(Modifier.height(8.dp))
        Wrap {
            Primary(t("Find links"), Icons.Filled.Link) {
                val c = Chains.get(chain)
                val list = text.split('\n', '\r', ',', ' ', ';').map { it.trim() }.filter { val d = Addr.detect(it); d.first == "address" && d.second == c.family }.map { Addr.norm(c.family, it) }.distinct()
                when { list.size < 2 -> toast(ctx, t("Enter at least two valid addresses for the selected network.")); list.size > 12 -> toast(ctx, t("Use at most 12 addresses at a time.")); else -> job = Triple((job?.first ?: 0) + 1, c, list) }
            }
            Plain(t("Fill with the demo addresses")) { text = Demo.hacker + "\n" + Demo.hacker2; chain = "demo" }
        }
    }
    val j = job ?: return
    Loader(j.first, { say ->
        val data = LinkedHashMap<String, List<Xfer>>()
        j.third.forEachIndexed { i, a -> say(tf("Reading address {0} of {1}…", i + 1, j.third.size)); try { Labels.learn(j.second.api.info(a)) } catch (_: ApiException) { }; data[a] = j.second.api.transfers(a, 300, false).items }
        Analysis.link(data, skip).shared.take(6).forEachIndexed { i, sh -> say(tf("Reading labels of shared counterparties… {0}", i + 1)); try { Labels.learn(j.second.api.info(sh.first)) } catch (_: ApiException) { } }
        Analysis.link(data, skip)
    }) { r ->
        val ck = j.second.key
        Wrap { Stat(t("Direct transfers"), "${r.direct.size}", color = if (r.direct.isNotEmpty()) C.Red else C.Text); Stat(t("Shared counterparties"), "${r.shared.size}"); Stat(t("Common first funders"), "${r.funders.size}", color = if (r.funders.isNotEmpty()) C.Red else C.Text) }
        r.funders.forEach { f -> Notice("high", t("Same first funder"), Labels.show(f.first) + "  →  " + f.second.joinToString(", ") { Fmt.short(it) }) { Nav.go(Sc.Address(ck, f.first)) } }
        if (r.direct.isNotEmpty()) Card(t("Direct transfers between the addresses"), t("Funds moving directly between two of your addresses is the strongest link.")) { r.direct.sortedBy { it.time }.forEach { XferRow(it, null, ck) } }
        if (r.shared.isNotEmpty()) Card(t("Shared counterparties"), t("An unlabelled wallet used by several of your addresses (for example the same deposit address) deserves a closer look.")) {
            r.shared.take(40).forEach { sh ->
                Row(Modifier.fillMaxWidth().clickable { Nav.go(Sc.Address(ck, sh.first)) }.padding(vertical = 7.dp), verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f)) {
                        Text(Labels.show(sh.first), color = C.Brand, fontWeight = FontWeight.SemiBold, fontSize = 13.5.sp, style = TextStyle(textDirection = TextDirection.Ltr))
                        Text(sh.second.joinToString(", ") { Fmt.short(it) }, fontSize = 11.5.sp, color = C.Muted, style = TextStyle(textDirection = TextDirection.Ltr), maxLines = 2, overflow = TextOverflow.Ellipsis)
                    }
                    Text("${sh.second.size} · ${sh.third} tx", fontSize = 12.5.sp, color = C.Text)
                }
                HorizontalDivider(color = Color(0xFFEDF0F3))
            }
        }
        if (r.direct.isEmpty() && r.shared.isEmpty() && r.funders.isEmpty()) Notice("ok", t("No link found in the loaded transfers"), t("That does not prove the addresses are unrelated: only the latest 500 transfers per type of each address were compared.").replace("500", "300"))
    }
}

@Composable fun MatchScreen() {
    val ctx = LocalContext.current
    var chain by remember { mutableStateOf(Store.defaultChain) }
    var suspect by remember { mutableStateOf("") }
    var service by remember { mutableStateOf("") }
    var chain2 by remember { mutableStateOf(Store.defaultChain) }
    var service2 by remember { mutableStateOf("") }
    var window by remember { mutableStateOf("24") }
    var tol by remember { mutableStateOf("1") }
    class Job(val n: Int, val c1: Chain, val c2: Chain, val s: String, val v1: String, val v2: String, val win: Long, val tol: Double)
    var job by remember { mutableStateOf<Job?>(null) }
    Card(t("Match deposits and withdrawals"), t("When funds enter a bridge or a privacy tool the trail breaks. This tool pairs each deposit made by the suspect with later withdrawals of almost the same amount, and tells you how many candidates fit.")) {
        ChainPicker(chain) { chain = it }
        Field(t("Suspect address (the depositor)"), suspect, { suspect = it }, code = true)
        Field(t("Service address the deposit went to"), service, { service = it }, code = true)
        Picker(t("or choose"), listOf("" to t("Pick a known service…")) + Labels.builtin.filter { it.value.cat == "mixer" || it.value.cat == "bridge" }.map { it.key to it.value.name }, "") { if (it.isNotEmpty()) service = it }
        Picker(t("Payout network"), Chains.all.map { it.key to it.name }, chain2) { chain2 = it }
        Field(t("Service address that pays out (empty = same)"), service2, { service2 = it }, code = true, hint = t("For a bridge this is the bridge's address on the destination network."))
        Wrap {
            Picker(t("Time window"), listOf("1" to "1 h", "6" to "6 h", "24" to "24 h", "72" to "3 d", "168" to "7 d"), window) { window = it }
            Picker(t("Amount tolerance"), listOf("0.1", "0.5", "1", "2", "5").map { it to "$it %" }, tol) { tol = it }
        }
        Wrap {
            Primary(t("Find matches"), Icons.AutoMirrored.Filled.CompareArrows) {
                val c1 = Chains.get(chain); val c2 = Chains.get(chain2)
                val s = Addr.norm(c1.family, suspect); val v1 = Addr.norm(c1.family, service)
                val v2 = if (service2.isBlank()) (if (c1 === c2) v1 else "") else Addr.norm(c2.family, service2)
                when {
                    Addr.detect(s).first != "address" || Addr.detect(v1).first != "address" -> toast(ctx, t("Enter the suspect address and the service address."))
                    v2.isEmpty() -> toast(ctx, t("Enter the service address on the payout network."))
                    else -> job = Job((job?.n ?: 0) + 1, c1, c2, s, v1, v2, window.toLong() * 3600, tol.toDouble())
                }
            }
            Plain(t("Fill with the demo")) { suspect = Demo.hacker; service = Demo.mixer; service2 = ""; chain = "demo"; chain2 = "demo" }
        }
    }
    val j = job ?: return
    Loader(j.n, { say ->
        say(t("Reading the suspect's deposits…"))
        val deps = j.c1.api.transfers(j.s, 1000, false).items.filter { it.from == j.s && it.to == j.v1 }
        if (deps.isEmpty()) return@Loader Triple(deps, emptyList<MatchRow>(), 0)
        say(t("Reading the service's payouts in the time window…"))
        val wds = j.c2.api.transfers(j.v2, 2000, true, deps.minOf { it.time }, deps.maxOf { it.time } + j.win).items.filter { it.from == j.v2 && it.to != j.s }
        Triple(deps, Analysis.match(deps, wds, j.win, j.tol), wds.size)
    }) { (deps, rows, wds) ->
        if (deps.isEmpty()) { Notice("medium", t("No deposit found"), t("The suspect address did not send anything directly to that service address in its latest 1000 transfers.")); return@Loader }
        Wrap { Stat(t("Deposits"), "${deps.size}"); Stat(t("Payouts examined"), "$wds"); Stat(t("Strong matches"), "${rows.count { it.confidence == "high" }}", color = C.Green) }
        if (rows.isEmpty()) { Notice("medium", t("No payout fits"), t("Try a longer time window or a larger tolerance (fees can be several percent).")); return@Loader }
        Card(t("Candidate pairs"), t("“Strong” means a single payout fits and the amount is not a round number. Double-click to open the receiving address.").substringBefore(". ") + ".") {
            rows.sortedBy { when (it.confidence) { "high" -> 0; "medium" -> 1; else -> 2 } }.forEach { m ->
                Column(Modifier.fillMaxWidth().clickable { Nav.go(Sc.Address(j.c2.key, m.wd.to)) }.padding(vertical = 8.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Chip(t(when (m.confidence) { "high" -> "Strong"; "medium" -> "Medium"; else -> "Weak" }), when (m.confidence) { "high" -> C.Green; "medium" -> C.Amber; else -> C.Gray })
                        Text(Labels.show(m.wd.to), color = C.Brand, fontWeight = FontWeight.SemiBold, fontSize = 13.5.sp, style = TextStyle(textDirection = TextDirection.Ltr))
                    }
                    Text("${Fmt.amt(m.dep.amount)} ${m.dep.asset}  →  ${Fmt.amt(m.wd.amount)} ${m.wd.asset}", fontSize = 13.5.sp, style = TextStyle(textDirection = TextDirection.Ltr))
                    Text(t("Delay") + ": " + Fmt.dur(m.delay) + " · " + t("Difference") + ": " + "%.2f".format(java.util.Locale.US, m.diffPct) + " % · " + t("Candidates") + ": " + m.candidates, fontSize = 12.sp, color = C.Muted)
                }
                HorizontalDivider(color = Color(0xFFEDF0F3))
            }
        }
        Notice("info", t("Limits of this method"), t("Fixed-amount pools (such as 1, 10 or 100 ETH) make amounts useless, so only timing remains and matches are weak. A match is a lead to corroborate with other evidence."))
    }
}

@Composable fun WatchScreen() {
    val ctx = LocalContext.current
    val scope = rememberCoroutineScope()
    var rev by remember { mutableIntStateOf(0) }
    var addr by remember { mutableStateOf("") }
    var chain by remember { mutableStateOf(if (Store.defaultChain == "demo") "eth" else Store.defaultChain) }
    var busy by remember { mutableStateOf(false) }
    var on by remember { mutableStateOf(Store.watchOn) }
    val perm = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { }
    rev.let { }
    Card(t("Watchlist"), t("Athar checks these addresses while it is open and alerts you when they move funds — useful for dormant wallets holding stolen money.").replace(" while it is open", "")) {
        Field(t("Address"), addr, { addr = it }, code = true)
        ChainPicker(chain, demo = false) { chain = it }
        Wrap {
            Primary(t("Add"), Icons.Filled.Add) {
                val c = Chains.get(chain); val d = Addr.detect(addr)
                if (d.first != "address" || d.second != c.family) toast(ctx, t("Enter a valid address for the selected network."))
                else if (Store.watch.any { it.address == Addr.norm(c.family, addr) && it.chain == chain }) toast(ctx, t("Already on the watchlist"))
                else {
                    Store.watch += WatchItem(chain, Addr.norm(c.family, addr), "", System.currentTimeMillis() / 1000); Store.save(); addr = ""; rev++
                    WatchWorker.schedule(ctx)
                    if (Build.VERSION.SDK_INT >= 33) perm.launch(Manifest.permission.POST_NOTIFICATIONS)
                }
            }
            Plain(if (busy) t("Checking…") else t("Check now"), Icons.Filled.Refresh) {
                if (Store.watch.isEmpty()) toast(ctx, t("The watchlist is empty."))
                else if (!busy) { busy = true; scope.launch { val n = Store.checkWatch(); busy = false; rev++; toast(ctx, if (n > 0) tf("{0} new transfer(s) on watched addresses", n) else t("No new transfers.")) } }
            }
        }
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(top = 6.dp)) {
            Switch(on, { on = it; Store.watchOn = it; Store.save(); WatchWorker.schedule(ctx); if (it && Build.VERSION.SDK_INT >= 33) perm.launch(Manifest.permission.POST_NOTIFICATIONS) })
            Column(Modifier.padding(start = 10.dp)) { Text(t("Background alerts"), fontWeight = FontWeight.SemiBold, fontSize = 14.sp); Text(t("Check watched addresses about every 15 minutes and notify me."), fontSize = 12.sp, color = C.Muted) }
        }
    }
    if (Store.alerts.isNotEmpty()) Card(t("Alerts")) {
        Store.alerts.take(40).forEach { a -> XferRow(a.x, a.watched, a.x.chain) }
        TextButton({ Store.alerts.clear(); Store.save(); rev++ }) { Text(t("Clear alerts")) }
    }
    Card(t("Watched addresses")) {
        if (Store.watch.isEmpty()) Text(t("Nothing is being watched yet. Add an address above, or press “Watch” on any address profile."), fontSize = 13.sp, color = C.Muted)
        Store.watch.toList().forEach { w ->
            Row(Modifier.fillMaxWidth().clickable { Nav.go(Sc.Address(w.chain, w.address)) }.padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text(Labels.show(w.address), color = C.Brand, fontWeight = FontWeight.SemiBold, fontSize = 13.5.sp, style = TextStyle(textDirection = TextDirection.Ltr))
                    Text(Chains.get(w.chain).name + " · " + t("Checked up to (UTC)") + " " + Fmt.time(w.lastTime), fontSize = 11.5.sp, color = C.Muted)
                }
                IconButton({ Store.watch.remove(w); Store.save(); rev++ }) { Icon(Icons.Filled.Delete, t("Stop watching"), tint = C.Muted) }
            }
            HorizontalDivider(color = Color(0xFFEDF0F3))
        }
    }
}

@Composable fun AskDialog(title: String, label: String, initial: String = "", lines: Int = 1, onDone: (String?) -> Unit) {
    var v by remember { mutableStateOf(initial) }
    AlertDialog(onDismissRequest = { onDone(null) }, title = { Text(title, fontSize = 18.sp) }, text = { Field(label, v, { v = it }, lines = lines) },
        confirmButton = { TextButton({ onDone(v.trim()) }) { Text(t("Save")) } }, dismissButton = { TextButton({ onDone(null) }) { Text(t("Cancel")) } })
}

@Composable fun CasesScreen() {
    var rev by remember { mutableIntStateOf(0) }
    var adding by remember { mutableStateOf(false) }
    if (adding) AskDialog(t("New case"), t("Case name")) { name -> adding = false; if (!name.isNullOrEmpty()) Nav.go(Sc.CaseView(Store.newCase(name).id)) }
    rev.let { }
    Primary(t("New case"), Icons.Filled.Add) { adding = true }
    Spacer(Modifier.height(10.dp))
    if (Store.cases.isEmpty()) Card { Steps("Create a case and give it a name.", "While you search and trace, press “Add to case” or “Save to case”.", "Open the case, write your summary and findings, then export the report.") }
    Store.cases.toList().forEach { c ->
        Card(modifier = Modifier.clickable { Nav.go(Sc.CaseView(c.id)) }) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text(c.name, fontSize = 16.sp, fontWeight = FontWeight.SemiBold)
                    if (c.id == Store.activeCase) Chip(t("Active — new items go here"), C.Green)
                    Text(tf("{0} item(s) · {1} trace(s) · opened {2}", c.items.size, c.traces.size, Fmt.day(c.created)), fontSize = 12.5.sp, color = C.Muted)
                }
                if (c.id != Store.activeCase) TextButton({ Store.activeCase = c.id; Store.save(); rev++ }) { Text(t("Make active")) }
            }
        }
    }
}

@Composable fun CaseScreen(s: Sc.CaseView) {
    val c = Store.cases.firstOrNull { it.id == s.id } ?: run { Text(t("Nothing here yet."), color = C.Muted); return }
    val ctx = LocalContext.current
    var rev by remember { mutableIntStateOf(0) }
    var notes by remember(rev) { mutableStateOf(c.notes) }
    var addFinding by remember { mutableStateOf(false) }
    var confirmDelete by remember { mutableStateOf(false) }
    var editing by remember { mutableStateOf<CaseItem?>(null) }
    if (addFinding) AskDialog(t("Add finding"), t("Finding (one sentence, with the evidence it rests on)"), lines = 3) { v -> addFinding = false; if (!v.isNullOrEmpty()) { c.findings += v; Store.save(); rev++ } }
    editing?.let { it0 -> AskDialog(t("Edit role and note"), t("Role (victim, suspect, hop, exchange deposit …)"), it0.role) { v -> if (v != null) { it0.role = v; Store.save(); rev++ }; editing = null } }
    if (confirmDelete) AlertDialog(onDismissRequest = { confirmDelete = false }, title = { Text(tf("Delete case “{0}”? This cannot be undone.", c.name), fontSize = 16.sp) },
        confirmButton = { TextButton({ Store.cases.remove(c); if (Store.activeCase == c.id) Store.activeCase = ""; Store.save(); confirmDelete = false; Nav.back() }) { Text(t("Delete"), color = C.Red) } }, dismissButton = { TextButton({ confirmDelete = false }) { Text(t("Cancel")) } })
    Wrap {
        SaveReport(t("Save report (HTML)"), c.name.replace(Regex("[^\\w\\u0600-\\u06FF]+"), "_") + ".html") { Report.caseHtml(c, Store.examiner, uiLang == "ar") { t(it) } }
        Plain(t("Delete"), Icons.Filled.Delete, color = C.Red) { confirmDelete = true }
    }
    Spacer(Modifier.height(8.dp))
    Card(t("Summary"), t("What happened, when, how much, and what you are trying to establish.")) {
        Field(t("Summary"), notes, { notes = it }, lines = 4)
        Primary(t("Save summary"), Icons.Filled.Save) { c.notes = notes; Store.save(); toast(ctx, t("Saved")) }
    }
    AiCard(t("AI draft of the case summary"), t("Use as the summary"), { text -> c.notes = text; Store.save(); rev++ }) { Ai.casePrompt(c) }
    Card(t("Findings")) {
        if (c.findings.isEmpty()) Text(t("No findings written yet."), fontSize = 13.sp, color = C.Muted)
        c.findings.toList().forEachIndexed { i, f ->
            Row(verticalAlignment = Alignment.CenterVertically) { Text("${i + 1}. $f", Modifier.weight(1f), fontSize = 13.5.sp, lineHeight = 20.sp); IconButton({ c.findings.removeAt(i); Store.save(); rev++ }) { Icon(Icons.Filled.Delete, t("Remove"), tint = C.Muted) } }
        }
        Plain(t("Add finding"), Icons.Filled.Add) { addFinding = true }
    }
    Card(t("Addresses and transactions")) {
        if (c.items.isEmpty()) Text(t("No addresses or transactions yet. Use “Add to case” on any address or transaction page."), fontSize = 13.sp, color = C.Muted)
        c.items.toList().forEach { it0 ->
            Row(Modifier.fillMaxWidth().clickable { Nav.go(if (it0.kind == "tx") Sc.Tx(it0.chain, it0.value) else Sc.Address(it0.chain, it0.value)) }.padding(vertical = 5.dp), verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text(if (it0.kind == "address") Labels.show(it0.value) else Fmt.short(it0.value), color = C.Brand, fontWeight = FontWeight.SemiBold, fontSize = 13.5.sp, style = TextStyle(textDirection = TextDirection.Ltr))
                    Text(t(if (it0.kind == "tx") "Transaction" else "Address") + " · " + (Chains.all.firstOrNull { ch -> ch.key == it0.chain }?.name ?: it0.chain) + if (it0.role.isNotEmpty()) " · " + it0.role else "", fontSize = 11.5.sp, color = C.Muted)
                }
                TextButton({ editing = it0 }) { Text(t("Role")) }
                IconButton({ c.items.remove(it0); Store.save(); rev++ }) { Icon(Icons.Filled.Delete, t("Remove"), tint = C.Muted) }
            }
            HorizontalDivider(color = Color(0xFFEDF0F3))
        }
    }
    Card(t("Saved traces")) {
        if (c.traces.isEmpty()) Text(t("No saved traces. Run a trace and press “Save to case”."), fontSize = 13.sp, color = C.Muted)
        c.traces.toList().forEach { g ->
            Row(Modifier.fillMaxWidth().clickable { Nav.go(Sc.Trace(graph = g)) }.padding(vertical = 5.dp), verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text(Labels.show(g.nodes.firstOrNull()?.id ?: g.start), color = C.Brand, fontWeight = FontWeight.SemiBold, fontSize = 13.5.sp, style = TextStyle(textDirection = TextDirection.Ltr))
                    Text(t(if (g.forward) "Forward" else "Backward") + " · " + tf("{0} addresses, {1} flows", g.nodes.size, g.edges.size) + " · " + Fmt.day(g.created), fontSize = 11.5.sp, color = C.Muted)
                }
                IconButton({ c.traces.remove(g); Store.save(); rev++ }) { Icon(Icons.Filled.Delete, t("Remove"), tint = C.Muted) }
            }
        }
    }
}

@Composable fun LabelsScreen() {
    var rev by remember { mutableIntStateOf(0) }
    var adding by remember { mutableStateOf(false) }
    var editing by remember { mutableStateOf<String?>(null) }
    val ctx = LocalContext.current
    if (adding) AskDialog(t("Add label"), t("Address")) { a -> adding = false; if (a != null) { val d = Addr.detect(a); if (d.first == "address") editing = Addr.norm(d.second, a) else toast(ctx, t("This is not a valid address or transaction hash.")) } }
    editing?.let { LabelDialog(it) { editing = null; rev++ } }
    rev.let { }
    Primary(t("Add label"), Icons.Filled.Add) { adding = true }
    Spacer(Modifier.height(10.dp))
    Card(t("My labels")) {
        if (Labels.user.isEmpty()) Text(t("You have no labels yet. Press “Label” on any address, or import a CSV file: address,name,category,note.").substringBefore(", or") + ".", fontSize = 13.sp, color = C.Muted)
        Labels.user.toList().forEach { u ->
            Row(Modifier.fillMaxWidth().clickable { editing = u.address }.padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) { Text(u.name, fontWeight = FontWeight.SemiBold, fontSize = 14.sp); Text(catName(u.cat) + " · " + Fmt.short(u.address), fontSize = 11.5.sp, color = C.Muted, style = TextStyle(textDirection = TextDirection.Ltr)) }
                IconButton({ Labels.user.remove(u); Store.save(); rev++ }) { Icon(Icons.Filled.Delete, t("Delete"), tint = C.Muted) }
            }
            HorizontalDivider(color = Color(0xFFEDF0F3))
        }
    }
    Card(t("Built-in list (Ethereum)"), t("A short list of well-known services. Most labels come live from the explorer; always confirm a label on the explorer before citing it.")) {
        Labels.builtin.entries.sortedBy { it.value.cat }.forEach { (a, l) ->
            Row(Modifier.padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) { Text(l.name, Modifier.weight(1f), fontSize = 13.5.sp); Chip(catName(l.cat), C.cat(l.cat)); Code(Fmt.short(a), size = 11, color = C.Muted) }
        }
    }
}

@Composable fun SettingsScreen() {
    val ctx = LocalContext.current
    val scope = rememberCoroutineScope()
    var rev by remember { mutableIntStateOf(0) }
    var examiner by remember { mutableStateOf(Store.examiner) }
    var eKey by remember { mutableStateOf(Chains.etherscanKey) }
    var rKey by remember { mutableStateOf(Store.relayKey) }
    var cKey by remember { mutableStateOf("") }
    val hasKey = remember(rev) { SecureKey.load(ctx).isNotBlank() }
    var model by remember { mutableStateOf(Store.claudeModel) }
    Card(t("General")) {
        Text(t("Language"), fontSize = 12.5.sp, color = C.Muted, fontWeight = FontWeight.SemiBold)
        Row(Modifier.padding(vertical = 6.dp)) {
            listOf("ar" to "عربي", "en" to "English").forEach { (k, l) -> if (uiLang == k) Primary(l) { } else Plain(l) { uiLang = k; Store.lang = k; Store.save() } }
        }
        Field(t("Examiner name (printed on reports)"), examiner, { examiner = it })
        Field(t("Etherscan API key (optional)"), eKey, { eKey = it }, code = true, hint = t("Only needed for Optimism, Base, Arbitrum, Polygon and BNB Chain. Create a free key at etherscan.io → API Keys. It is stored on this computer only.").substringBefore(" It is stored"))
        Field(t("Relay API key (optional)"), rKey, { rKey = it }, code = true, hint = t("Relay bridge lookups work without a key through an older endpoint that is slow and being retired; a key from relay.link keeps them working."))
        Wrap {
            Primary(t("Save"), Icons.Filled.Save) { Store.examiner = examiner.trim(); Store.relayKey = rKey.trim(); Http.hostHeaders["api.relay.link"] = "x-api-key" to Store.relayKey; Chains.etherscanKey = eKey.trim(); Chains.resetAll(); Store.save(); toast(ctx, t("Saved")) }
            Plain(t("Clear downloaded data")) { Http.cacheDir?.listFiles()?.forEach { it.delete() }; toast(ctx, t("Cleared")) }
        }
    }
    Card(t("AI analysis (Claude API) — optional"), t("Explains an address, a trace or a whole case in plain language and suggests next steps. Everything else in Athar works without it.")) {
        Notice(if (hasKey) "ok" else "info", if (hasKey) t("A Claude API key is saved on this phone (encrypted).") else t("No Claude API key saved yet."))
        Field(t("Claude API key"), cKey, { cKey = it }, code = true, hint = t("Create a key at platform.claude.com → API keys. It is encrypted on this phone and is sent only to the Claude API. Usage is billed to your Anthropic account."))
        Picker(t("Model"), Ai.models.map { it to it }, model) { model = it }
        Text(t("claude-opus-5-5 is the most capable; sonnet and haiku are faster and cheaper."), fontSize = 12.sp, color = C.Muted)
        Spacer(Modifier.height(8.dp))
        Wrap {
            Primary(t("Save"), Icons.Filled.Save) { if (cKey.isNotBlank()) SecureKey.save(ctx, cKey); cKey = ""; Store.claudeModel = model; Store.save(); rev++; toast(ctx, t("Saved")) }
            if (hasKey) Plain(t("Test the key")) { toast(ctx, t("Asking Claude…")); scope.launch { try { Ai.ask(SecureKey.load(ctx), model, "Reply with the single word: ready", false); toast(ctx, t("The key works.")) } catch (e: Exception) { toast(ctx, e.message ?: "error") } } }
            if (hasKey) Plain(t("Remove the key"), color = C.Red) { SecureKey.save(ctx, ""); rev++ }
        }
    }
    Card(t("How on-chain investigators work"), t("The same open-source steps this tool supports.")) {
        Steps("Follow the outflows: from the theft address through swaps and hops until a service that can freeze or identify.", "Look for test transactions: a tiny transfer shortly before a large one to the same address.", "Look for commingling: funds from two incidents meeting in one wallet point to one operator.", "Match amount and timing across a bridge or privacy tool; unique amounts and short delays are the reliable cases.", "Tie on-chain behaviour to off-chain evidence, archive what you cite, and state your confidence.")
    }
    Notice("medium", t("Use responsibly"), t("Results are investigative leads. An address is not a person; accusing someone publicly on the basis of a trace alone can be wrong and harmful. Report thefts to the exchange concerned and to the police."))
    Card(t("About")) {
        Text(t("Developed by Dr. Mohammed Tawfik"), fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
        Text("Athar 1.0.0 · MIT License", fontSize = 12.5.sp, color = C.Muted)
        Text(t("Athar reads public data from blockchain explorers. It never asks for a wallet, a seed phrase or a private key, and it cannot move funds."), fontSize = 13.sp, color = C.Text, lineHeight = 20.sp, modifier = Modifier.padding(top = 6.dp))
    }
}
