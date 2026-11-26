package app.athar

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.SystemBarStyle
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.AccountTree
import androidx.compose.material.icons.filled.Build
import androidx.compose.material.icons.filled.Menu
import androidx.compose.material.icons.filled.Notifications
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.Badge
import androidx.compose.material3.BadgedBox
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.NavigationBarItemDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.key
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.app.NotificationCompat
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.NetworkType
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import java.util.concurrent.TimeUnit

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        enableEdgeToEdge(statusBarStyle = SystemBarStyle.dark(C.Brand.toArgb()), navigationBarStyle = SystemBarStyle.light(Color.White.toArgb(), Color.White.toArgb()))
        super.onCreate(savedInstanceState)
        Store.init(filesDir)
        uiLang = Store.lang
        WatchWorker.schedule(this)
        handle(intent)
        setContent { App() }
    }

    override fun onNewIntent(intent: Intent) { super.onNewIntent(intent); handle(intent) }

    /** Text shared from another app (an address or a hash) opens directly. "demo" extra is used by automated checks. */
    private fun handle(i: Intent?) {
        if (i == null) return
        i.getStringExtra("athar_open")?.let { v -> if (v == "demo") { Nav.tab(Sc.Home); Nav.go(Sc.Address("demo", Demo.hacker)) } else if (v == "trace") { Nav.tab(Sc.Trace(TraceOptions("demo", Demo.hacker, true))) } else if (v == "settings") { Nav.tab(Sc.More); Nav.go(Sc.Settings) } else if (v == "home") Nav.tab(Sc.Home) }
        i.getStringExtra("athar_lang")?.let { uiLang = it; Store.lang = it }
        if (i.action == Intent.ACTION_SEND) i.getStringExtra(Intent.EXTRA_TEXT)?.let { txt ->
            val token = txt.split(Regex("\\s+")).firstOrNull { Addr.detect(it).first != "unknown" }
            if (token != null) { Nav.tab(Sc.Home); search(token, Store.defaultChain) }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable fun App() {
    val cur = Nav.stack.last()
    BackHandler(Nav.stack.size > 1) { Nav.back() }
    CompositionLocalProvider(LocalLayoutDirection provides if (uiLang == "ar") LayoutDirection.Rtl else LayoutDirection.Ltr) {
        MaterialTheme(colorScheme = lightColorScheme(primary = C.Brand, onPrimary = Color.White, background = C.Bg, surface = Color.White, onSurface = C.Text, secondaryContainer = C.BrandSoft, onSecondaryContainer = C.BrandDark, surfaceVariant = C.Bg, outline = Color(0xFFC5CDD6))) {
            val title = when (cur) {
                Sc.Home -> if (uiLang == "ar") "أثر" else "Athar"
                is Sc.Address -> t("Address profile"); is Sc.Tx -> t("Transaction"); is Sc.Trace -> t("Trace funds"); Sc.Tools -> t("Tools"); is Sc.Link -> t("Link addresses"); Sc.Match -> t("Match in / out")
                Sc.Watch -> t("Watchlist"); Sc.More -> t("More"); Sc.Cases -> t("Cases & reports"); is Sc.CaseView -> Store.cases.firstOrNull { it.id == cur.id }?.name ?: t("Case"); Sc.LabelsSc -> t("Labels"); Sc.Settings -> t("Settings")
            }
            val root = Nav.stack.first()
            Scaffold(
                containerColor = C.Bg,
                topBar = {
                    TopAppBar(title = { Text(title, fontWeight = FontWeight.SemiBold, fontSize = 19.sp, maxLines = 1) },
                        navigationIcon = { if (Nav.stack.size > 1) IconButton({ Nav.back() }) { Icon(Icons.AutoMirrored.Filled.ArrowBack, t("Back")) } },
                        colors = TopAppBarDefaults.topAppBarColors(containerColor = C.Brand, titleContentColor = Color.White, navigationIconContentColor = Color.White))
                },
                bottomBar = {
                    NavigationBar(containerColor = Color.White) {
                        val items = listOf(Triple(Sc.Home as Sc, t("Search"), Icons.Filled.Search), Triple(Sc.Trace() as Sc, t("Trace funds"), Icons.Filled.AccountTree), Triple(Sc.Tools as Sc, t("Tools"), Icons.Filled.Build), Triple(Sc.Watch as Sc, t("Watchlist"), Icons.Filled.Notifications), Triple(Sc.More as Sc, t("More"), Icons.Filled.Menu))
                        items.forEach { (sc, label, icon) ->
                            val sel = root::class == sc::class
                            NavigationBarItem(selected = sel, onClick = { Nav.tab(sc) }, label = { Text(label, fontSize = 11.sp, maxLines = 1) },
                                icon = { if (sc == Sc.Watch && Store.alerts.isNotEmpty()) BadgedBox({ Badge { Text("${Store.alerts.size.coerceAtMost(99)}") } }) { Icon(icon, null) } else Icon(icon, null) },
                                colors = NavigationBarItemDefaults.colors(selectedIconColor = C.BrandDark, selectedTextColor = C.BrandDark, indicatorColor = C.BrandSoft, unselectedIconColor = C.Muted, unselectedTextColor = C.Muted))
                        }
                    }
                },
            ) { pad ->
                key(cur) {
                    Column(Modifier.fillMaxSize().background(C.Bg).padding(pad).imePadding().verticalScroll(rememberScrollState()).padding(horizontal = 12.dp, vertical = 12.dp)) {
                        when (cur) {
                            Sc.Home -> HomeScreen(); is Sc.Address -> AddressScreen(cur); is Sc.Tx -> TxScreen(cur); is Sc.Trace -> TraceScreen(cur); Sc.Tools -> ToolsScreen(); is Sc.Link -> LinkScreen(cur); Sc.Match -> MatchScreen()
                            Sc.Watch -> WatchScreen(); Sc.More -> MoreScreen(); Sc.Cases -> CasesScreen(); is Sc.CaseView -> CaseScreen(cur); Sc.LabelsSc -> LabelsScreen(); Sc.Settings -> SettingsScreen()
                        }
                    }
                }
            }
        }
    }
}

/** Periodic background check of the watchlist; posts a notification when a watched address moved funds. */
class WatchWorker(ctx: Context, p: WorkerParameters) : CoroutineWorker(ctx, p) {
    override suspend fun doWork(): Result {
        Store.init(applicationContext.filesDir)
        if (!Store.watchOn || Store.watch.isEmpty()) return Result.success()
        val n = try { Store.checkWatch() } catch (_: Exception) { 0 }
        if (n > 0) notify(applicationContext, n)
        return Result.success()
    }

    companion object {
        fun schedule(ctx: Context) {
            val wm = WorkManager.getInstance(ctx)
            if (!Store.watchOn || Store.watch.isEmpty()) { wm.cancelUniqueWork("athar_watch"); return }
            val req = PeriodicWorkRequestBuilder<WatchWorker>(15, TimeUnit.MINUTES).setConstraints(Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build()).build()
            wm.enqueueUniquePeriodicWork("athar_watch", ExistingPeriodicWorkPolicy.KEEP, req)
        }

        fun notify(ctx: Context, n: Int) {
            val nm = ctx.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
            nm.createNotificationChannel(NotificationChannel("watch", "Watchlist", NotificationManager.IMPORTANCE_DEFAULT))
            val ar = Store.lang == "ar"
            val pi = PendingIntent.getActivity(ctx, 0, Intent(ctx, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
            val x = Store.alerts.firstOrNull()?.x
            val b = NotificationCompat.Builder(ctx, "watch").setSmallIcon(R.drawable.ic_notify).setColor(0xFF1565C0.toInt())
                .setContentTitle(if (ar) "عنوان مراقَب حرّك أموالًا" else "Watched address moved funds")
                .setContentText((if (ar) "$n تحويل جديد" else "$n new transfer(s)") + (x?.let { " · " + Fmt.amt(it.amount) + " " + it.asset } ?: ""))
                .setContentIntent(pi).setAutoCancel(true)
            try { nm.notify(1, b.build()) } catch (_: SecurityException) { }
        }
    }
}
