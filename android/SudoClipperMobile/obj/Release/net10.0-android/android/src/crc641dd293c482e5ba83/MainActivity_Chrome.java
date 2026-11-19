package crc641dd293c482e5ba83;


public class MainActivity_Chrome
	extends android.webkit.WebChromeClient
	implements
		mono.android.IGCUserPeer
{

	public MainActivity_Chrome ()
	{
		super ();
		if (getClass () == MainActivity_Chrome.class) {
			mono.android.TypeManager.Activate ("SudoClipperMobile.MainActivity+Chrome, SudoClipperMobile", "", this, new java.lang.Object[] {  });
		}
	}

	public MainActivity_Chrome (crc641dd293c482e5ba83.MainActivity p0)
	{
		super ();
		if (getClass () == MainActivity_Chrome.class) {
			mono.android.TypeManager.Activate ("SudoClipperMobile.MainActivity+Chrome, SudoClipperMobile", "SudoClipperMobile.MainActivity, SudoClipperMobile", this, new java.lang.Object[] { p0 });
		}
	}

	public boolean onShowFileChooser (android.webkit.WebView p0, android.webkit.ValueCallback p1, android.webkit.WebChromeClient.FileChooserParams p2)
	{
		return n_onShowFileChooser (p0, p1, p2);
	}

	private native boolean n_onShowFileChooser (android.webkit.WebView p0, android.webkit.ValueCallback p1, android.webkit.WebChromeClient.FileChooserParams p2);

	private java.util.ArrayList refList;
	public void monodroidAddReference (java.lang.Object obj)
	{
		if (refList == null)
			refList = new java.util.ArrayList ();
		refList.add (obj);
	}

	public void monodroidClearReferences ()
	{
		if (refList != null)
			refList.clear ();
	}
}
