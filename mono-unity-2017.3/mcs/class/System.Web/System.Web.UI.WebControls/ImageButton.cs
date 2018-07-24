//
// System.Web.UI.WebControls.ImageButton.cs
//
// Authors:
//	Jordi Mas i Hernandez (jordi@ximian.com)
//
// (C) 2005 Novell, Inc (http://www.novell.com)
//
//
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System.Collections.Specialized;
using System.ComponentModel;
using System.Security.Permissions;

namespace System.Web.UI.WebControls {

	// CAS
	[AspNetHostingPermissionAttribute (SecurityAction.LinkDemand, Level = AspNetHostingPermissionLevel.Minimal)]
	[AspNetHostingPermissionAttribute (SecurityAction.InheritanceDemand, Level = AspNetHostingPermissionLevel.Minimal)]
	// attributes
	[DefaultEvent("Click")]
#if NET_2_0
	[Designer ("System.Web.UI.Design.WebControls.PreviewControlDesigner, " + Consts.AssemblySystem_Design, "System.ComponentModel.Design.IDesigner")]
	[SupportsEventValidation]
	public class ImageButton : Image, IPostBackDataHandler, IPostBackEventHandler, IButtonControl {
#else
	public class ImageButton : Image, IPostBackDataHandler, IPostBackEventHandler {
#endif
		static readonly object ClickEvent = new object ();
		static readonly object CommandEvent = new object ();
		int pos_x, pos_y;

		public ImageButton ()
		{

		}

#if ONLY_1_1
		[Bindable(false)]
#endif		
		[DefaultValue(true)]
		[WebSysDescription ("")]
		[WebCategory ("Behavior")]
#if NET_2_0
		[Themeable (false)]
		public virtual
#else		
		public
#endif		
		bool CausesValidation {
			get {
				return ViewState.GetBool ("CausesValidation", true);
			}

			set {
				ViewState ["CausesValidation"] = value;
			}
		}

		[Bindable(true)]
		[DefaultValue("")]
		[WebSysDescription ("")]
		[WebCategory ("Behavior")]
#if NET_2_0
		[Themeable (false)]
#endif		
		public string CommandArgument {
			get {
				return ViewState.GetString ("CommandArgument", "");
			}
			set {
				ViewState ["CommandArgument"] = value;
			}
		}

		[DefaultValue("")]
		[WebSysDescription ("")]
		[WebCategory ("Behavior")]
#if NET_2_0
		[Themeable (false)]
#endif		
		public string CommandName {
			get {
				return ViewState.GetString ("CommandName", "");
			}
			set {
				ViewState ["CommandName"] = value;
			}
		}

#if NET_2_0
		[EditorBrowsable (EditorBrowsableState.Always)]
		[Browsable (true)]
		[DefaultValue (true)]
		[Bindable (true)]
		public virtual new bool Enabled
		{
			// Should there be any special code below? Doesn't look so...
			get { return base.Enabled; }
			set { base.Enabled = value; }
		}

		[Browsable (false)]
		[EditorBrowsable (EditorBrowsableState.Never)]
		[Themeable (false)]
		[DesignerSerializationVisibility (DesignerSerializationVisibility.Hidden)]
		public override bool GenerateEmptyAlternateText
		{
			get { return false; }
			set { throw new NotSupportedException (); }
		}

		[DefaultValue ("")]
		[Themeable (false)]
		public virtual string OnClientClick 
		{
			get {
				return ViewState.GetString ("OnClientClick", String.Empty);
			}
			set {
				ViewState ["OnClientClick"] = value;
			}
		}

		[Themeable (false)]
#if NET_2_0
		[UrlProperty ("*.aspx")]
#else
		[UrlProperty]
#endif
		[DefaultValue ("")]
		[Editor ("System.Web.UI.Design.UrlEditor, "  + Consts.AssemblySystem_Design, "System.Drawing.Design.UITypeEditor, " + Consts.AssemblySystem_Drawing)]
		public virtual string PostBackUrl {
			get {
				return ViewState.GetString ("PostBackUrl", String.Empty);
			}
			set {
				ViewState["PostBackUrl"] = value;
			}
		}

		[Themeable (false)]
		[DefaultValue ("")]
		[WebSysDescription ("")]
		[WebCategory ("Behavior")]
		public virtual string ValidationGroup
		{
			get {
				return ViewState.GetString ("ValidationGroup", "");
			}
			set {
				ViewState ["ValidationGroup"] = value;
			}
		}
#endif		

		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
#if NET_2_0 && HAVE_CONTROL_ADAPTERS
		protected virtual new
#else		
		protected override
#endif
		HtmlTextWriterTag TagKey {
			get { return HtmlTextWriterTag.Input; }
		}

#if NET_2_0
		// Gets or sets the value of the ImageButton control's AlternateText property. (MSDN)
		protected virtual string Text 
		{
			get {
				return AlternateText;
			}
			set {
				AlternateText = value;
			}
		}
#endif		

		protected override void AddAttributesToRender (HtmlTextWriter writer)
		{			
			Page page = Page;
			if (page != null)
				page.VerifyRenderingInServerForm (this);
			
			writer.AddAttribute (HtmlTextWriterAttribute.Type, "image", false);
			writer.AddAttribute (HtmlTextWriterAttribute.Name, UniqueID);

			base.AddAttributesToRender (writer);
#if NET_2_0
			string onclick = OnClientClick;
			if (!String.IsNullOrEmpty (onclick))
				onclick = ClientScriptManager.EnsureEndsWithSemicolon (onclick);
			else
				onclick = String.Empty;
			
			if (HasAttributes && Attributes ["onclick"] != null) {
				onclick = ClientScriptManager.EnsureEndsWithSemicolon (onclick + Attributes ["onclick"]);
				Attributes.Remove ("onclick");
			}
			
			if (page != null)
				onclick += GetClientScriptEventReference ();
			
			if (onclick.Length > 0)
				writer.AddAttribute (HtmlTextWriterAttribute.Onclick, onclick);
#else
			if (CausesValidation && page != null && page.AreValidatorsUplevel ()) {
				ClientScriptManager csm = new ClientScriptManager (page);
				writer.AddAttribute (HtmlTextWriterAttribute.Onclick, csm.GetClientValidationEvent ());
				writer.AddAttribute ("language", "javascript");
			}
#endif
		}

#if NET_2_0
		internal virtual string GetClientScriptEventReference ()
		{
			PostBackOptions options = GetPostBackOptions ();
			Page page = Page;
			
			if (options.PerformValidation || !String.IsNullOrEmpty (options.ActionUrl)) {
				return page != null ? page.ClientScript.GetPostBackEventReference (options, true) : String.Empty;
			} else {
				if (page != null)
					page.ClientScript.RegisterForEventValidation (options);
				return String.Empty;
			}
		}

		protected virtual PostBackOptions GetPostBackOptions ()
		{
			PostBackOptions options = new PostBackOptions (this);
			Page page = Page;
			
			options.ActionUrl = (PostBackUrl.Length > 0 ?
#if TARGET_J2EE
					     CreateActionUrl (PostBackUrl)
#else
					     (page != null ? page.ResolveClientUrl (PostBackUrl) : null)
#endif
					     : null);

			options.Argument = String.Empty;
			options.ClientSubmit = true;
			options.RequiresJavaScriptProtocol = true;
			options.PerformValidation = CausesValidation && page != null && page.AreValidatorsUplevel (ValidationGroup);
			if (options.PerformValidation)
				options.ValidationGroup = ValidationGroup;
			else
				options.ValidationGroup = null;
			
			return options;
		}
#endif		


#if NET_2_0
		protected virtual
#endif
		bool LoadPostData (string postDataKey, NameValueCollection postCollection) 
		{
			string x, y;
			string unique = UniqueID;
			x = postCollection [unique + ".x"];
			y = postCollection [unique + ".y"];
			if (x != null && x != "" && y != null && y != "") {
				pos_x = Int32.Parse(x);
				pos_y = Int32.Parse(y);
				Page.RegisterRequiresRaiseEvent (this);
				return true;
			} else {
				x = postCollection [unique];
				if (x != null && x != "") {
					pos_x = Int32.Parse (x);
					pos_y = 0;
					Page.RegisterRequiresRaiseEvent (this);
					return true;
				}
			}

			return false;
		}
#if NET_2_0
		protected virtual
#endif
		void RaisePostDataChangedEvent ()
		{
		}
		
#if NET_2_0
		protected virtual
#endif
		void RaisePostBackEvent (string eventArgument)
		{
#if NET_2_0
			ValidateEvent (UniqueID, String.Empty);
#endif
			if (CausesValidation)
#if NET_2_0
				Page.Validate (ValidationGroup);
#else
				Page.Validate ();
#endif

			OnClick (new ImageClickEventArgs (pos_x, pos_y));
			OnCommand (new CommandEventArgs (CommandName, CommandArgument));
		}

		bool IPostBackDataHandler.LoadPostData (string postDataKey, NameValueCollection postCollection)
		{
			return LoadPostData (postDataKey, postCollection);
		}


		void IPostBackDataHandler.RaisePostDataChangedEvent ()
		{
			RaisePostDataChangedEvent ();
		}

		void IPostBackEventHandler.RaisePostBackEvent (string eventArgument)
		{
			RaisePostBackEvent (eventArgument);
		}

		protected virtual void OnClick (ImageClickEventArgs e)
		{
			if (Events != null) {
				ImageClickEventHandler eh = (ImageClickEventHandler) (Events [ClickEvent]);
				if (eh != null)
					eh (this, e);
			}
		}

		protected virtual void OnCommand (CommandEventArgs e)
		{
			if (Events != null) {
				CommandEventHandler eh = (CommandEventHandler) (Events [CommandEvent]);
				if (eh != null)
					eh (this, e);
			}

			RaiseBubbleEvent (this, e);
		}

#if NET_2_0
		protected internal
#else		
		protected
#endif		
		override void OnPreRender (EventArgs e)
		{
			if (Page != null && Enabled)
				Page.RegisterRequiresPostBack (this);
		}

		[WebSysDescription ("")]
		[WebCategory ("Action")]
		public event ImageClickEventHandler Click
		{
			add {
				Events.AddHandler (ClickEvent, value);
			}
			remove {
				Events.RemoveHandler (ClickEvent, value);
			}
		}

		[WebSysDescription ("")]
		[WebCategory ("Action")]
		public event CommandEventHandler Command
		{
			add {
				Events.AddHandler (CommandEvent, value);
			}
			remove {
				Events.RemoveHandler (CommandEvent, value);
			}
		}

#if NET_2_0
		string IButtonControl.Text 
		{
			get {
				return Text;
			}
			set {
				Text = value;
			}
		}

		event EventHandler IButtonControl.Click
		{
			add {
				Events.AddHandler (ClickEvent, value);
			}
			remove {
				Events.RemoveHandler (ClickEvent, value);
			}
		}
		
#endif
	}
}


