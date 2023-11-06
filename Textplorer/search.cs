using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Runtime.InteropServices;

namespace Textplorer
{
    /// <summary>
    /// This class implements the tool window exposed by this package and hosts a user control.
    /// </summary>
    /// <remarks>
    /// In Visual Studio tool windows are composed of a frame (implemented by the shell) and a pane,
    /// usually implemented by the package implementer.
    /// <para>
    /// This class derives from the ToolWindowPane class provided from the MPF in order to use its
    /// implementation of the IVsUIElementPane interface.
    /// </para>
    /// </remarks>
    [Guid("d6e419aa-8e58-4347-81e0-9d872e22a4e8")]
    public class search : ToolWindowPane
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="search"/> class.
        /// </summary>
        public search() : base(null)
        {
            this.Caption = "Textplorer";

            // This is the user control hosted by the tool window; Note that, even if this class implements IDisposable,
            // we are not calling Dispose on this object. This is because ToolWindowPane calls Dispose on
            // the object returned by the Content property.
            var control = new searchControl();
            control.MatchEventHandler += ShowTitle;
            this.Content = control;
        }

        private void SearchOnEscKeyPressDownHandler(object o, EventArgs e)
        {
            HideThisWindow();
        }

        public void HideThisWindow()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            IVsWindowFrame windowFrame = (IVsWindowFrame)this.Frame;

            if (windowFrame != null)
            {
                // Close the ToolWindowPane
                windowFrame.Hide();
            }
        }

        private void ShowTitle(object sender, MatchEventArgs e)
        {
            if (null == e)
            {
                return;
            }

            if (0 == e.Matches)
            {
                this.Caption = "Textplorer";
            }
            else
            {
                this.Caption = $"Textplorer ({e.Matches})";
            }
        }
    }
}
