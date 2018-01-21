using System;
using System.ComponentModel.Design;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace botvs.script.upload
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class AutoUpload
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("bd8696ea-10fb-4d1d-8c08-f688200c22c7");

        public static readonly Regex BotvsToken = new Regex("botvs@([a-zA-Z0-9]{32})", RegexOptions.Compiled);
        public static readonly Regex BotvsResponse = new Regex("\"code\":(\\d{0,3})", RegexOptions.Compiled);

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly Package package;

        /// <summary>
        /// Initializes a new instance of the <see cref="AutoUpload"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        private AutoUpload(Package package)
        {
            if (package == null)
            {
                throw new ArgumentNullException("package");
            }

            this.package = package;

            OleMenuCommandService commandService = this.ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null)
            {
                var menuCommandID = new CommandID(CommandSet, CommandId);
                var menuItem = new MenuCommand(this.MenuItemCallback, menuCommandID);
                commandService.AddCommand(menuItem);
            }
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static AutoUpload Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private IServiceProvider ServiceProvider
        {
            get
            {
                return this.package;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static void Initialize(Package package)
        {
            Instance = new AutoUpload(package);
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void MenuItemCallback(object sender, EventArgs e)
        {
            var dte = this.ServiceProvider.GetService(typeof(DTE)) as DTE;
            if (dte.ActiveDocument == null)
            {
                PrintStatus("please open a botvs script file first...");
                return;
            }

            string script = File.ReadAllText(dte.ActiveDocument.FullName);
            if (!string.IsNullOrEmpty(script))
            {
                Match match = BotvsToken.Match(script);
                if (match.Success)
                {
                    string token = match.Groups[1].Value;
                    this.SyncScript(token, script.Substring(match.Groups[0].Index + match.Groups[0].Length));
                }
                else
                {
                    PrintStatus("invalid botvs token! - botvs@([a-zA-Z0-9]{32})");
                }
            }
            else
            {
                PrintStatus("invalid empty file! - " + dte.ActiveDocument.FullName);
            }
        }

        private void SyncScript(string token, string content)
        {
            try
            {
                string json = string.Format(
                    "token={0}&method=push&content={1}&version=0.0.1&client=visual studio 2017 community",
                    token,
                    HttpUtility.UrlEncode(content.Trim(), Encoding.UTF8));
                HttpClient httpClient = new HttpClient();
                HttpContent httpContent = new StringContent(json);

                httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
                httpContent.Headers.ContentType.CharSet = "utf-8";

                HttpResponseMessage response = httpClient.PostAsync("https://www.botvs.com/rsync", httpContent).Result;
                string responseContent = response.Content.ReadAsStringAsync().Result;
                Match match = BotvsResponse.Match(responseContent);
                int code;
                if (match.Success && Int32.TryParse(match.Groups[1].Value, out code) && code < 100)
                {
                    PrintStatus("upload successfully!" + responseContent);
                }
                else
                {
                    PrintStatus("upload failed!" + responseContent);
                }
            }
            catch (Exception ex)
            {
                PrintStatus(ex.Message);
            }
        }

        private void PrintStatus(string status)
        {
            IVsStatusbar statusBar = (IVsStatusbar)ServiceProvider.GetService(typeof(SVsStatusbar));

            // Make sure the status bar is not frozen  
            int frozen;

            statusBar.IsFrozen(out frozen);

            if (frozen != 0)
            {
                statusBar.FreezeOutput(0);
            }

            // Set the status bar text and make its display static.  
            statusBar.SetText(string.Format("[{0}][botvs] - {1}", DateTime.Now, status));
        }
    }
}
