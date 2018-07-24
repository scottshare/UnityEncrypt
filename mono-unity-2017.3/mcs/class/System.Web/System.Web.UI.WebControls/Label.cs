//
// System.Web.UI.WebControls.Label.cs
//
// Authors:
//	Miguel de Icaza (miguel@novell.com)
//
// (C) 2005 Novell, Inc (http://www.novell.com)
//
// TODO: Are we missing something in LoadViewState?
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

using System.ComponentModel;
using System.Security.Permissions;

namespace System.Web.UI.WebControls {

	// CAS
	[AspNetHostingPermission (SecurityAction.LinkDemand, Level = AspNetHostingPermissionLevel.Minimal)]
	[AspNetHostingPermission (SecurityAction.InheritanceDemand, Level = AspNetHostingPermissionLevel.Minimal)]
	// attributes
	[ControlBuilder(typeof(LabelControlBuilder))]
	[DataBindingHandler("System.Web.UI.Design.TextDataBindingHandler, " + Consts.AssemblySystem_Design)]
	[DefaultProperty("Text")]
	[Designer("System.Web.UI.Design.WebControls.LabelDesigner, " + Consts.AssemblySystem_Design, "System.ComponentModel.Design.IDesigner")]
	[ParseChildren (false)]
#if NET_2_0
	[ToolboxData("<{0}:Label runat=\"server\" Text=\"Label\"></{0}:Label>")]
	[ControlValueProperty ("Text", null)]
#else	
	[ToolboxData("<{0}:Label runat=server>Label</{0}:Label>")]
#endif		
	public class Label : WebControl
#if NET_2_0
	, ITextControl
#endif		
	{
		[Bindable(true)]
		[DefaultValue("")]
		[PersistenceMode(PersistenceMode.InnerDefaultProperty)]
#if NET_2_0
		[Localizable (true)]
#endif		
		[WebSysDescription ("")]
		[WebCategory ("Appearance")]
		public virtual string Text {
			get {
				return ViewState.GetString ("Text", "");
			}
			set {
				ViewState ["Text"] = value;
				if (HasControls ())
					Controls.Clear ();
			}
		}

		// Added in .net 1.1 sp1
#if NET_2_0
		[IDReferenceProperty (typeof (Control))]
		[TypeConverter (typeof (AssociatedControlConverter))]
		[Themeable (false)]
#endif		
		[DefaultValue("")]
		[WebSysDescription ("")]
		[WebCategory ("Accessibility")]
		public virtual string AssociatedControlID {
			get {
				return ViewState.GetString ("AssociatedControlID", "");
			}
			set {
				ViewState ["AssociatedControlID"] = value;
			}
		}

		protected override void LoadViewState (object savedState)
		{
			base.LoadViewState (savedState);

			// Make sure we clear child controls when this happens
			if (ViewState ["Text"] != null)
				Text = (string) ViewState ["Text"];
		}

		protected override void AddParsedSubObject (object obj)
		{
			if (HasControls ()) {
				base.AddParsedSubObject (obj);
				return;
			}
			
			LiteralControl lc = obj as LiteralControl;

			if (lc == null) {
				string s = Text;
				if (s.Length != 0) {
					Text = null;
					Controls.Add (new LiteralControl (s));
				}
				base.AddParsedSubObject (obj);
			} else {
				Text = lc.Text;
			}
		}
		
#if NET_2_0
		protected internal
#else		
		protected
#endif		
		override void RenderContents (HtmlTextWriter writer)
		{
			if (HasControls () || HasRenderMethodDelegate ())
				base.RenderContents (writer);
			else
				writer.Write (Text);
		}

		// Added in .net 1.1 sp1
		protected override HtmlTextWriterTag TagKey {
			get {
				return AssociatedControlID == "" ? HtmlTextWriterTag.Span : HtmlTextWriterTag.Label;
			}
		}

		// Added in .net 1.1 sp1
		protected override void AddAttributesToRender (HtmlTextWriter writer)
		{
			base.AddAttributesToRender (writer);
			if (AssociatedControlID != "")
				writer.AddAttribute (HtmlTextWriterAttribute.For, NamingContainer.FindControl (AssociatedControlID).ClientID);
		}
	}
}
