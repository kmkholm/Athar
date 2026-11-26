package app.athar

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextDirection
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.CancellationException

object C {
    val Brand = Color(0xFF1565C0); val BrandDark = Color(0xFF0D47A1); val BrandSoft = Color(0xFFE3EEFB); val Bg = Color(0xFFF4F6F8); val Line = Color(0xFFDDE2E8)
    val Text = Color(0xFF1F2933); val Muted = Color(0xFF66788A); val Green = Color(0xFF2E7D32); val Red = Color(0xFFC62828); val Purple = Color(0xFF6A1B9A)
    val Orange = Color(0xFFE65100); val Gray = Color(0xFF78909C); val Teal = Color(0xFF00796B); val RedSoft = Color(0xFFFDECEA); val AmberSoft = Color(0xFFFFF4E0); val Amber = Color(0xFFB26A00); val GreenSoft = Color(0xFFE8F5E9)
    fun cat(cat: String) = when (cat) { "exchange" -> Green; "mixer", "threat" -> Red; "bridge" -> Purple; "dex" -> Orange; "user" -> Brand; "other" -> Teal; else -> Gray }
}

/** Current language as Compose state, so every screen redraws when it changes. */
var uiLang by mutableStateOf("ar")
fun t(key: String): String = if (uiLang == "ar") Tr.ar[key] ?: key else key
fun tf(key: String, vararg a: Any): String = a.foldIndexed(t(key)) { i, s, v -> s.replace("{$i}", v.toString()) }
fun catName(cat: String) = t(when (cat) { "exchange" -> "Exchange"; "mixer" -> "Mixer"; "bridge" -> "Bridge"; "dex" -> "DEX / swap"; "threat" -> "Flagged"; "contract" -> "Contract"; "user" -> "My label"; "other" -> "Other"; else -> "Unlabelled" })
fun statusName(s: String) = t(when (s) { "start" -> "Start"; "frozen" -> "Frozen by the issuer"; "holding" -> "Still holding (dormant)"; "exchange" -> "Exchange"; "mixer" -> "Mixer"; "bridge" -> "Bridge"; "dex" -> "Swapped (DEX)"; "burn" -> "Burned"; "otherchain" -> "On a network Athar cannot read"; "unexplored" -> "Not followed further"; else -> "Passed on" })
fun statusColor(s: String) = when (s) { "start" -> C.Brand; "frozen" -> Color(0xFF00838F); "holding" -> C.Amber; "exchange" -> C.Green; "mixer" -> C.Red; "bridge" -> C.Purple; "dex" -> C.Orange; "burn" -> C.Teal; "otherchain" -> C.Purple; "unexplored" -> Color(0xFFB0BEC5); else -> C.Gray }
fun money(usd: java.math.BigDecimal): String { val v = usd.toDouble(); return when { v >= 1e9 -> "$" + "%.2f".format(java.util.Locale.US, v / 1e9).trimEnd('0').trimEnd('.') + "B"; v >= 1e6 -> "$" + "%.1f".format(java.util.Locale.US, v / 1e6).removeSuffix(".0") + "M"; v >= 1e3 -> "$" + "%.1f".format(java.util.Locale.US, v / 1e3).removeSuffix(".0") + "K"; else -> "$" + "%.0f".format(java.util.Locale.US, v) } }
fun stopName(s: String) = t(when (s) { "service" -> "Stopped: known service"; "contract" -> "Stopped: contract"; "busy" -> "Stopped: very active wallet (likely a service)"; "hops" -> "Hop limit reached"; "end" -> "No later outflow"; "small" -> "Only small transfers"; "error" -> "Could not be read"; "burn" -> "Burn / null address"; else -> "" })

@Composable fun Card(title: String? = null, sub: String? = null, modifier: Modifier = Modifier, content: @Composable ColumnScope.() -> Unit) {
    Surface(modifier.fillMaxWidth().padding(bottom = 12.dp), shape = RoundedCornerShape(8.dp), color = Color.White, border = BorderStroke(1.dp, C.Line)) {
        Column(Modifier.padding(14.dp)) {
            if (title != null) Text(title, fontSize = 16.sp, fontWeight = FontWeight.SemiBold, color = C.Text)
            if (sub != null) Text(sub, fontSize = 12.5.sp, color = C.Muted, lineHeight = 18.sp)
            if (title != null || sub != null) Spacer(Modifier.height(10.dp))
            content()
        }
    }
}

@Composable fun Chip(text: String, color: Color) {
    Text(text, Modifier.padding(end = 6.dp, bottom = 4.dp).clip(RoundedCornerShape(10.dp)).background(color.copy(alpha = 0.12f)).padding(horizontal = 9.dp, vertical = 2.dp), color = color, fontSize = 12.sp, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
}
@Composable fun LabelChip(addr: String) { Labels.find(addr)?.let { Chip(if (it.name.isEmpty()) catName(it.cat) else it.name + " · " + catName(it.cat), C.cat(it.cat)) } }

/** Addresses and hashes always read left-to-right. */
@Composable fun Code(text: String, modifier: Modifier = Modifier, size: Int = 13, color: Color = C.Text, bold: Boolean = false, maxLines: Int = 3) {
    Text(text, modifier, color = color, fontSize = size.sp, fontFamily = FontFamily.Monospace, fontWeight = if (bold) FontWeight.SemiBold else FontWeight.Normal, style = TextStyle(textDirection = TextDirection.Ltr), maxLines = maxLines, overflow = TextOverflow.Ellipsis)
}

@Composable fun Notice(level: String, title: String, detail: String? = null, onClick: (() -> Unit)? = null) {
    val fg = when (level) { "high" -> C.Red; "medium" -> C.Amber; "ok" -> C.Green; else -> C.Brand }
    val bg = when (level) { "high" -> C.RedSoft; "medium" -> C.AmberSoft; "ok" -> C.GreenSoft; else -> C.BrandSoft }
    Row(Modifier.fillMaxWidth().padding(bottom = 8.dp).clip(RoundedCornerShape(6.dp)).background(bg).let { if (onClick != null) it.clickable(onClick = onClick) else it }.padding(12.dp)) {
        Box(Modifier.padding(top = 5.dp, end = 10.dp).size(9.dp).clip(CircleShape).background(fg))
        Column(Modifier.weight(1f)) {
            Text(title, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, color = C.Text)
            if (!detail.isNullOrEmpty()) Text(detail, fontSize = 13.sp, color = C.Text, lineHeight = 19.sp)
        }
    }
}

@Composable fun Primary(text: String, icon: ImageVector? = null, modifier: Modifier = Modifier, enabled: Boolean = true, onClick: () -> Unit) {
    Button(onClick, modifier.padding(end = 8.dp), enabled = enabled, shape = RoundedCornerShape(6.dp), colors = ButtonDefaults.buttonColors(containerColor = C.Brand)) {
        if (icon != null) { Icon(icon, null, Modifier.size(18.dp)); Spacer(Modifier.width(6.dp)) }
        Text(text, fontWeight = FontWeight.SemiBold)
    }
}
@Composable fun Plain(text: String, icon: ImageVector? = null, modifier: Modifier = Modifier, color: Color = C.Text, onClick: () -> Unit) {
    OutlinedButton(onClick, modifier.padding(end = 8.dp), shape = RoundedCornerShape(6.dp), border = BorderStroke(1.dp, if (color == C.Text) Color(0xFFC5CDD6) else color), contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 12.dp, vertical = 6.dp)) {
        if (icon != null) { Icon(icon, null, Modifier.size(17.dp), tint = color); Spacer(Modifier.width(6.dp)) }
        Text(text, color = color, fontSize = 13.5.sp)
    }
}

@Composable fun Field(label: String, value: String, onChange: (String) -> Unit, modifier: Modifier = Modifier.fillMaxWidth(), code: Boolean = false, lines: Int = 1, number: Boolean = false, hint: String? = null) {
    Column(modifier.padding(bottom = 10.dp)) {
        OutlinedTextField(value, onChange, Modifier.fillMaxWidth(), label = { Text(label, fontSize = 13.sp) }, singleLine = lines == 1, minLines = lines,
            textStyle = if (code) TextStyle(fontFamily = FontFamily.Monospace, fontSize = 13.5.sp, textDirection = TextDirection.Ltr) else TextStyle(fontSize = 15.sp),
            keyboardOptions = if (number) KeyboardOptions(keyboardType = KeyboardType.Number) else KeyboardOptions.Default, shape = RoundedCornerShape(6.dp))
        if (hint != null) Text(hint, fontSize = 12.sp, color = C.Muted, modifier = Modifier.padding(top = 3.dp))
    }
}

@Composable fun Picker(label: String, items: List<Pair<String, String>>, selected: String, modifier: Modifier = Modifier, onPick: (String) -> Unit) {
    var open by remember { mutableStateOf(false) }
    Column(modifier.padding(end = 8.dp, bottom = 10.dp)) {
        Text(label, fontSize = 12.5.sp, color = C.Muted, fontWeight = FontWeight.SemiBold)
        Box {
            OutlinedButton({ open = true }, shape = RoundedCornerShape(6.dp), border = BorderStroke(1.dp, Color(0xFFC5CDD6)), contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 12.dp, vertical = 6.dp)) {
                Text(items.firstOrNull { it.first == selected }?.second ?: selected, color = C.Text, fontSize = 14.sp, maxLines = 1); Text("  ▾", color = C.Muted)
            }
            DropdownMenu(open, { open = false }) { items.forEach { (k, l) -> DropdownMenuItem({ Text(l) }, { open = false; onPick(k) }) } }
        }
    }
}
@Composable fun ChainPicker(selected: String, demo: Boolean = true, onPick: (String) -> Unit) =
    Picker(t("Network"), Chains.all.filter { demo || it.key != "demo" }.map { it.key to it.name + if (it.needsKey && !it.ready) "  (" + t("needs key") + ")" else "" }, selected, onPick = onPick)

@OptIn(ExperimentalLayoutApi::class)
@Composable fun Wrap(content: @Composable () -> Unit) = FlowRow(verticalArrangement = Arrangement.Center) { content() }

@Composable fun Stat(title: String, value: String, sub: String? = null, color: Color = C.Text, modifier: Modifier = Modifier) {
    Surface(modifier.padding(end = 8.dp, bottom = 8.dp), shape = RoundedCornerShape(8.dp), color = Color.White, border = BorderStroke(1.dp, C.Line)) {
        Column(Modifier.padding(horizontal = 12.dp, vertical = 10.dp)) {
            Text(title, fontSize = 12.sp, color = C.Muted, fontWeight = FontWeight.SemiBold)
            Text(value, fontSize = 18.sp, fontWeight = FontWeight.SemiBold, color = color, style = TextStyle(textDirection = TextDirection.Ltr), maxLines = 1)
            if (sub != null) Text(sub, fontSize = 11.5.sp, color = C.Muted)
        }
    }
}

@Composable fun Ltr(content: @Composable () -> Unit) = CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr, content = content)

/** Runs suspend work and shows progress, the error, or the result. Re-runs when key changes. */
@Composable fun <T> Loader(key: Any?, work: suspend ((String) -> Unit) -> T, content: @Composable (T) -> Unit) {
    var state by remember(key) { mutableStateOf<Result<T>?>(null) }
    var msg by remember(key) { mutableStateOf("") }
    var attempt by remember(key) { mutableIntStateOf(0) }
    LaunchedEffect(key, attempt) {
        state = null
        state = try { Result.success(work { msg = it }) } catch (e: CancellationException) { throw e } catch (e: Exception) { Result.failure(e) }
    }
    val s = state
    when {
        s == null -> Card {
            Text(msg.ifEmpty { t("Loading from the blockchain explorer…") }, fontSize = 13.sp, color = C.Muted)
            LinearProgressIndicator(Modifier.fillMaxWidth().padding(top = 10.dp), color = C.Brand, trackColor = C.BrandSoft)
        }
        s.isFailure -> Column { Notice("high", t("Could not load the data"), s.exceptionOrNull()?.message); TextButton({ attempt++ }) { Text(t("Retry")) } }
        else -> content(s.getOrThrow())
    }
}

@Composable fun Steps(vararg steps: String) = Column {
    steps.forEachIndexed { i, s ->
        Row(Modifier.padding(bottom = 6.dp)) {
            Box(Modifier.padding(end = 10.dp).size(22.dp).clip(CircleShape).background(C.BrandSoft), contentAlignment = Alignment.Center) { Text("${i + 1}", fontSize = 12.sp, fontWeight = FontWeight.SemiBold, color = C.BrandDark) }
            Text(t(s), fontSize = 13.5.sp, color = C.Text, lineHeight = 20.sp, modifier = Modifier.weight(1f))
        }
    }
}
