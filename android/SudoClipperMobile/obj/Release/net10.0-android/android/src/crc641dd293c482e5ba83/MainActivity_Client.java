package crc641dd293c482e5ba83;


public class MainActivity_Client
	extends android.webkit.WebViewClient
	implements
		mono.android.IGCUserPeer
{

	public MainActivity_Client ()
	{
		super ();
		if (getClass () == MainActivity_Client.class) {
			mono.android.TypeManager.Activate ("SudoClipperMobile.MainActivity+Client, SudoClipperMobile", "", this, new java.lang.Object[] {  });
		}
	}

	public MainActivity_Client (crc641dd293c482e5ba83.MainActivity p0)
	{
		super ();
		if (getClass () == MainActivity_Client.class) {
			mono.android.TypeManager.Activate ("SudoClipperMobile.MainActivity+Client, SudoClipperMobile", "SudoClipperMobile.MainActivity, SudoClipperMobile", this, new java.lang.Object[] { p0 });
		}
	}

	public void onReceivedError (android.webkit.WebView p0, android.webkit.WebResourceRequest p1, android.webkit.WebResourceError p2)
	{
		n_onReceivedError (p0, p1, p2);
	}

	private native void n_onReceivedError (android.webkit.WebView p0, android.webkit.WebResourceRequest p1, android.webkit.WebResourceError p2);

	public boolean shouldOverrideUrlLoading (android.webkit.WebView p0, android.webkit.WebResourceRequest p1)
	{
		return n_shouldOverrideUrlLoading (p0, p1);
	}

	private native boolean n_shouldOverrideUrlLoading (android.webkit.WebView p0, android.webkit.WebResourceRequest p1);

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
