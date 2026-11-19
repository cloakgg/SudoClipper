using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Android.Widget;

[assembly: UsesPermission(Android.Manifest.Permission.Internet)]

namespace SudoClipperMobile;

[Application(Label = "SUDOCLIPPER", UsesCleartextTraffic = true, AllowBackup = true,
    Theme = "@android:style/Theme.Material.NoActionBar")]
public class MobileApp : Application
{
    public MobileApp() { }

    protected MobileApp(IntPtr handle, Android.Runtime.JniHandleOwnership transfer)
        : base(handle, transfer) { }
}

/// <summary>
/// Prototype Android shell: the whole SUDOCLIPPER studio (clipper, chat, memory, config)
/// inside a WebView that talks to a SUDOCLIPPER server on the local network.
/// </summary>
[Activity(Label = "SUDOCLIPPER", MainLauncher = true,
    Theme = "@android:style/Theme.Material.NoActionBar",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize |
                           ConfigChanges.SmallestScreenSize | ConfigChanges.KeyboardHidden,
    WindowSoftInputMode = SoftInput.AdjustResize)]
public class MainActivity : Activity
{
    private const string Prefs = "sudoclipper";
    private const string UrlKey = "server";
    private const int PickFile = 71;

    private WebView? web;
    private EditText? input;
    private IValueCallback? pendingFileCallback;
    private string currentUrl = "";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.SetStatusBarColor(Color.ParseColor("#050505"));
        Window?.SetNavigationBarColor(Color.ParseColor("#050505"));

        var saved = GetSharedPreferences(Prefs, FileCreationMode.Private)?.GetString(UrlKey, "") ?? "";
        if (string.IsNullOrWhiteSpace(saved)) ShowConnect(saved, "Start SUDOCLIPPER on your computer, then enter its address.");
        else ShowWeb(saved);
    }

    private void ShowConnect(string prefill, string note)
    {
        currentUrl = prefill;

        var root = new FrameLayout(this);
        root.SetBackgroundColor(Color.ParseColor("#050505"));

        var column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        column.SetGravity(GravityFlags.CenterHorizontal);
        column.SetPadding(Dp(28), Dp(72), Dp(28), Dp(28));

        var mark = new TextView(this)
        {
            Text = "SUDOCLIPPER",
            TextSize = 26,
            Typeface = Typeface.Create("sans-serif-black", TypefaceStyle.Bold),
        };
        mark.SetTextColor(Color.White);
        mark.LetterSpacing = 0.12f;
        column.AddView(mark);

        var sub = new TextView(this)
        {
            Text = "Android preview",
            TextSize = 10,
        };
        sub.SetTextColor(Color.ParseColor("#8A8A8A"));
        sub.LetterSpacing = 0.24f;
        column.AddView(sub);

        var gap = new Space(this);
        column.AddView(gap, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(40)));

        var noteView = new TextView(this) { Text = note, TextSize = 13 };
        noteView.SetTextColor(Color.ParseColor("#BDBDBD"));
        noteView.SetLineSpacing(0f, 1.3f);
        column.AddView(noteView);

        input = new EditText(this)
        {
            Hint = "http://192.168.1.20:4319",
            Text = prefill,
            TextSize = 14,
        };
        input.SetSingleLine(true);
        input.SetTextColor(Color.White);
        input.SetHintTextColor(Color.ParseColor("#5F5F5F"));
        input.SetBackgroundColor(Color.ParseColor("#101010"));
        input.SetPadding(Dp(14), Dp(12), Dp(14), Dp(12));
        var inputParams = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = Dp(16),
        };
        column.AddView(input, inputParams);

        var connect = new Button(this) { Text = "Connect" };
        connect.SetTextColor(Color.Black);
        connect.SetBackgroundColor(Color.White);
        connect.Click += (_, _) => Connect(input.Text ?? "");
        var connectParams = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(46))
        {
            TopMargin = Dp(12),
        };
        column.AddView(connect, connectParams);

        var emulator = new Button(this) { Text = "Use emulator address (10.0.2.2:4319)" };
        emulator.SetTextColor(Color.ParseColor("#BDBDBD"));
        emulator.SetBackgroundColor(Color.ParseColor("#101010"));
        emulator.Click += (_, _) =>
        {
            if (input != null) input.Text = "http://10.0.2.2:4319";
            Connect("http://10.0.2.2:4319");
        };
        var emuParams = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(42))
        {
            TopMargin = Dp(8),
        };
        column.AddView(emulator, emuParams);

        var hint = new TextView(this)
        {
            Text = "The address is printed by the SUDOCLIPPER server. Both devices must share the same Wi-Fi, " +
                   "and the server has to be listening on the network (not just this PC).",
            TextSize = 11,
        };
        hint.SetTextColor(Color.ParseColor("#6F6F6F"));
        hint.SetLineSpacing(0f, 1.4f);
        var hintParams = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = Dp(24),
        };
        column.AddView(hint, hintParams);

        root.AddView(column, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        SetContentView(root);
    }

    private void Connect(string raw)
    {
        var url = Normalize(raw);
        if (url.Length == 0)
        {
            Toast.MakeText(this, "Enter the SUDOCLIPPER address", ToastLength.Short)?.Show();
            return;
        }

        GetSharedPreferences(Prefs, FileCreationMode.Private)?.Edit()?.PutString(UrlKey, url)?.Apply();
        ShowWeb(url);
    }

    private static string Normalize(string raw)
    {
        var url = (raw ?? "").Trim().TrimEnd('/');
        if (url.Length == 0) return "";
        if (!url.Contains("://", StringComparison.Ordinal)) url = "http://" + url;
        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal) + 3;
        if (url.IndexOf(':', schemeEnd) < 0 && url.IndexOf('/', schemeEnd) < 0) url += ":4319";
        return url;
    }

    private void ShowWeb(string url)
    {
        currentUrl = url;

        var root = new FrameLayout(this);
        root.SetBackgroundColor(Color.ParseColor("#050505"));

        web = new WebView(this);
        var settings = web.Settings;
        settings.JavaScriptEnabled = true;
        settings.DomStorageEnabled = true;
        settings.DatabaseEnabled = true;
        settings.MediaPlaybackRequiresUserGesture = false;
        settings.MixedContentMode = MixedContentHandling.AlwaysAllow;
        settings.AllowFileAccess = true;
        settings.LoadWithOverviewMode = true;
        settings.UseWideViewPort = true;

        web.SetBackgroundColor(Color.ParseColor("#050505"));
        web.SetWebViewClient(new Client(this));
        web.SetWebChromeClient(new Chrome(this));
        web.LoadUrl(url);

        root.AddView(web, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        // Long-press anywhere without a link reopens the address screen.
        var handle = new Handler(Looper.MainLooper!);
        web.LongClick += (_, args) =>
        {
            args.Handled = true;
            handle.Post(() => ShowConnect(currentUrl, "Change the server this phone talks to."));
        };

        SetContentView(root);
    }

    public void ShowOffline(string reason)
    {
        RunOnUiThread(() => ShowConnect(currentUrl,
            $"Could not reach {currentUrl}.\n{reason}\nCheck the address, Wi-Fi, and that SUDOCLIPPER is running."));
    }

    public void BeginFilePick(IValueCallback? callback)
    {
        pendingFileCallback = callback;
        try
        {
            var intent = new Intent(Intent.ActionGetContent);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("*/*");
            StartActivityForResult(Intent.CreateChooser(intent, "Pick a video"), PickFile);
        }
        catch
        {
            pendingFileCallback = null;
            Toast.MakeText(this, "No file picker available", ToastLength.Short)?.Show();
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PickFile) return;

        var uri = resultCode == Result.Ok ? data?.Data : null;
        pendingFileCallback?.OnReceiveValue(uri != null ? new[] { uri } : null);
        pendingFileCallback = null;
    }

    public override void OnBackPressed()
    {
        if (web != null && web.CanGoBack()) web.GoBack();
        else base.OnBackPressed();
    }

    private int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density);

    private sealed class Client : WebViewClient
    {
        private readonly MainActivity owner;
        public Client(MainActivity owner) => this.owner = owner;

        public override void OnReceivedError(WebView? view, IWebResourceRequest? request, WebResourceError? error)
        {
            base.OnReceivedError(view, request, error);
            if (request?.IsForMainFrame != true) return;
            owner.ShowOffline(error?.Description?.ToString() ?? "Network error");
        }

        public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
        {
            var url = request?.Url?.ToString() ?? "";
            // Keep the studio itself in-app; anything else opens in the phone browser.
            if (url.Length == 0 || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                owner.currentUrl.Length > 0 && url.StartsWith(owner.currentUrl, StringComparison.OrdinalIgnoreCase))
                return false;
            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    owner.StartActivity(new Intent(Intent.ActionView, Android.Net.Uri.Parse(url)));
                    return true;
                }
                catch { }
            }
            return false;
        }
    }

    private sealed class Chrome : WebChromeClient
    {
        private readonly MainActivity owner;
        public Chrome(MainActivity owner) => this.owner = owner;

        public override bool OnShowFileChooser(WebView? webView, IValueCallback? filePathCallback, FileChooserParams? fileChooserParams)
        {
            owner.BeginFilePick(filePathCallback);
            return true;
        }
    }
}
