//
// System.Web.UI.WebControls.ButtonColumn.cs
//
// Author:
//      Dick Porter  <dick@ximian.com>
//      Gonzalo Paniagua Javier (gonzalo@ximian.com)
//
// Copyright (C) 2005 Novell, Inc (http://www.novell.com)
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
	public class ButtonColumn : DataGridColumn {
		       
		[DefaultValue(ButtonColumnType.LinkButton)]
#if NET_2_0
		[WebSysDescription("The type of button contained within the column.")]
#else
		[Description("The type of button contained within the column.")]
#endif
		[WebCategory ("Misc")]
		public virtual ButtonColumnType ButtonType
		{
			get {
				return (ButtonColumnType) ViewState.GetInt ("LinkButton",
						(int) ButtonColumnType.LinkButton);
			}
			set {
				ViewState ["LinkButton"] = value;
			}
		}
		
		[DefaultValue("")]
#if NET_2_0
		[WebSysDescription("The command associated with the button.")]
#else
		[Description("The command associated with the button.")]
#endif
		[WebCategory ("Misc")]
		public virtual string CommandName
		{
			get {
				return ViewState.GetString ("CommandName", String.Empty);
			}
			set {
				ViewState ["CommandName"] = value;
			}
		}

#if NET_2_0
		[DefaultValue (false)]
		[WebSysDescription("")]
		[WebCategory ("Behavior")]
		public virtual bool CausesValidation
		{
			get {
				return ViewState.GetBool ("CausesValidation", false);
			}
			set {
				ViewState ["CausesValidation"] = value;
			}
		}
#endif

		[DefaultValue("")]
#if NET_2_0
		[WebSysDescription("The field bound to the text property of the button.")]
#else
		[Description("The field bound to the text property of the button.")]
#endif
		[WebCategory ("Misc")]
		public virtual string DataTextField 
		{
			get {
				return ViewState.GetString ("DataTextField", String.Empty);
			}
			set {
				ViewState ["DataTextField"] = value;
			}
		}
		
		[DefaultValue("")]
#if NET_2_0
		[WebSysDescription("The formatting applied to the value bound to the Text property.")]
#else
		[Description("The formatting applied to the value bound to the Text property.")]
#endif
 		[WebCategory ("Misc")]
		public virtual string DataTextFormatString 
		{
			get {
				return ViewState.GetString ("DataTextFormatString",
						String.Empty);
			}
			set {
				ViewState ["DataTextFormatString"] = value;
				format = null;
			}
		}

		[DefaultValue("")]
#if NET_2_0
		[Localizable (true)]
		[WebSysDescription("The text used for the button.")]
#else
		[Description("The text used for the button.")]
#endif
		[WebCategory ("Misc")]
		public virtual string Text 
		{
			get {
				return ViewState.GetString ("Text", String.Empty);
			}
			set {
				ViewState ["Text"] = value;
			}
		}

#if NET_2_0
		[DefaultValue ("")]
		[WebSysDescription("")]
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
		
		public override void Initialize ()
		{
			/* No documentation for this method, so it's
			 * only here to keep corcompare quiet
			 */
			base.Initialize ();
		}

		public override void InitializeCell (TableCell cell,
						     int columnIndex,
						     ListItemType itemType)
		{
			base.InitializeCell (cell, columnIndex, itemType);

			if (itemType != ListItemType.Header &&
			    itemType != ListItemType.Footer) {
				switch (ButtonType) {
				case ButtonColumnType.LinkButton: 
				{
					LinkButton butt = new ForeColorLinkButton ();
					
					butt.Text = Text;
					butt.CommandName = CommandName;

					if (DataTextField != "")
						butt.DataBinding += new EventHandler (DoDataBind);

					cell.Controls.Add (butt);
				}
				break;

				case ButtonColumnType.PushButton: 
				{
					Button butt = new Button ();
					
					butt.Text = Text;
					butt.CommandName = CommandName;

					if (DataTextField != "")
						butt.DataBinding += new EventHandler (DoDataBind);
					cell.Controls.Add (butt);
				}
				break;
			
				}
			}
		}

		string text_field;
		string GetValueFromItem (DataGridItem item)
		{
			object val = null;
			if (text_field == null)
				text_field = DataTextField;

			if (text_field != "")
				val = DataBinder.Eval (item.DataItem, text_field);

			return FormatDataTextValue (val);
		}

		void DoDataBind (object sender, EventArgs e)
		{
			Control ctrl = (Control) sender;
			string text = GetValueFromItem ((DataGridItem) ctrl.NamingContainer);

			LinkButton lb = sender as LinkButton;
			if (lb == null) {
				Button b = (Button) sender;
				b.Text = text;
			} else {
				lb.Text = text;
			}
		}

		string format;
		protected virtual string FormatDataTextValue (object dataTextValue)
		{
			if (dataTextValue == null)
				return "";

			if (format == null)
				format = DataTextFormatString;

			if (format == "")
				return dataTextValue.ToString ();

			return String.Format (format, dataTextValue);
		}
	}
}

