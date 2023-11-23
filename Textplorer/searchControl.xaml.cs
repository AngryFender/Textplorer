using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Xml;

namespace Textplorer
{
    /// <summary>
    /// Interaction logic for searchControl.
    /// </summary>
    public partial class searchControl : UserControl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="searchControl"/> class.
        /// </summary>
        private readonly List<Item> emptyList = new List<Item>();
        private const int upperBoundLineNumber = 25;
        private readonly string[] banList = { ".filters", ".png", ".jpg", ".vsixmanifest",".dll" };
        private CancellationTokenSource cts = new CancellationTokenSource();
        public static StringToXamlConverter xamlConverter = new StringToXamlConverter();
        private const string BraceStart = "&#40;";
        private const string BraceEnd = "&#41;🔎";
        private const string RunStart = "<Run Style=\"{DynamicResource highlight}\">";
        private const string RunEnd = "</Run>";

        public searchControl()
        {
            this.InitializeComponent();

            inputBox.IsKeyboardFocusedChanged += VisibleChangedHandler;
            inputBox.KeyUp += KeyUpHandler;
            inputBox.TextChanged += InputBox_TextChanged;
            myListView.SelectionChanged += MyListView_SelectionChanged;
        }

        private void KeyUpHandler(object sender, KeyEventArgs e)
        {
            if(e.Key == Key.Enter)
            {
                if(0 < myListView.Items.Count)
                {
                    myListView.SelectedIndex = 0;
                    ListViewItem listViewItem = (ListViewItem)myListView.ItemContainerGenerator.ContainerFromIndex(0);
                    if(null != listViewItem)
                    {
                        listViewItem.Focus();
                    }
                }
            }
        }

        public event EventHandler<MatchEventArgs> MatchEventHandler;

        public void RaiseMatchEvent(int matches)
        {
            MatchEventHandler?.Invoke(this, new MatchEventArgs(matches));
        }

        private void VisibleChangedHandler(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.IsVisible && inputBox.IsKeyboardFocused)
            {
                inputBox.SelectAll();
                inputBox.Focus();
            }
        }

        private async void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                cts.Cancel();
                cts = new CancellationTokenSource();
                CancellationToken token = cts.Token;

                if (inputBox.Text.Length < 1)
                {
                    myListView.ItemsSource = emptyList;
                    RaiseMatchEvent(0);
                    return;
                }

                string searchText = inputBox.Text;
                var matchFiles = GetAllSolutionFiles(searchText, token);

                List<Item> tinyList = new List<Item>();
                var tinyTask = Task.Run(() => GetAllTinyMatchingItems(tinyList, matchFiles, searchText, token))
                    .ContinueWith(t =>
                    {
                        Dispatcher.Invoke(() => SetListViewSource(tinyList));
                    });
                
                List<Item> matchList = new List<Item>();
                var bigTask = Task.Run(() => GetAllMatchingItems(matchList, matchFiles, searchText, token))
                    .ContinueWith(t => 
                    {
                        Dispatcher.Invoke(() => SetListViewSource(matchList,true));
                    });

                await Task.WhenAll(tinyTask,bigTask);
            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void SetListViewSource(List<Item> list, bool showCount = false)
        {
            if(inputBox.Text.Length < 1)
            {
                myListView.ItemsSource = emptyList;
                RaiseMatchEvent(0);
            }
            else
            {
                myListView.ItemsSource= list;
                if (showCount)
                {
                    RaiseMatchEvent(list.Count);
                }
            }
        }

        private void GetAllTinyMatchingItems(List<Item> tinyList, List<(string, string)> matchFiles, string searchText, CancellationToken token)
        {
            foreach(var filePaths in matchFiles)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }
                SearchInLinesTinyResults(filePaths, searchText, tinyList, token);
            }
        }
        private void SearchInLinesTinyResults((string, string) filePaths, string searchText, List<Item> TinyList, CancellationToken token)
        {
            try
            {
                //skip if file doesnt contain the search text
                string fileContent = File.ReadAllText(filePaths.Item1);
                if (!fileContent.ToLower().Contains(searchText.ToLower()))
                {
                    return;
                }

                // Read all lines from the current file
                string[] lines = File.ReadAllLines(filePaths.Item1);
                int lineNumber = 0;
                string relativePath = filePaths.Item1;

                //if the path of the file includes the project name
                if (filePaths.Item1.Contains(filePaths.Item2))
                {
                     relativePath = filePaths.Item1.Substring(filePaths.Item1.LastIndexOf("\\" + filePaths.Item2 + "\\")).TrimStart('\\');
                }
                else
                {
                    //if the path of the file doesn't include the project name just take last 3 folders in the path
                    var pathParts = filePaths.Item1.Split(Path.DirectorySeparatorChar);
                    var lastThreeFoldersWithFile = pathParts.Skip(Math.Max(0, pathParts.Length - 4)).ToArray();
                    relativePath=  string.Join(Path.DirectorySeparatorChar.ToString(), lastThreeFoldersWithFile);
                }

                // Search for the string in each line
                string xmlContent = string.Empty;
                foreach (string line in lines)
                {
                    if (token.IsCancellationRequested || lineNumber >60)
                    {
                        return;
                    }
                    int index = line.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
                    if (index != -1)
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.Append(SecurityElement.Escape(relativePath))
                          .Append(BraceStart)
                          .Append((lineNumber + 1).ToString())
                          .Append(BraceEnd)
                          .Append(SecurityElement.Escape(line.Substring(0, index)))
                          .Append(RunStart)
                          .Append(SecurityElement.Escape(searchText))
                          .Append(RunEnd)
                          .Append(SecurityElement.Escape(line.Substring(index + searchText.Length)));

                        xmlContent = string.Format("<TextBlock xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TextWrapping=\"Wrap\">{0}</TextBlock>", sb.ToString());

                        Item listItem = new Item(filePaths.Item1, xmlContent, lineNumber, index);
                        TinyList.Add(listItem);
                    }
                    lineNumber++;
                }
            }
            catch (Exception ex)
            {
                // Handle any exceptions that may occur while processing the file
                Console.WriteLine($"Error processing file '{filePaths.Item1}': {ex.Message}");
            }
        }

        private void GetAllMatchingItems(List<Item> matchList, List<(string, string)> matchFiles, string searchText, CancellationToken token)
        {
            foreach(var filePaths in matchFiles)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }
                SearchInLines(filePaths, searchText, matchList, token);
            }
        }

        private void SearchInLines((string, string) filePaths, string searchText, List<Item> list, CancellationToken token)
        {
            try
            {
                //skip if file doesnt contain the search text
                string fileContent = File.ReadAllText(filePaths.Item1);
                if (!fileContent.ToLower().Contains(searchText.ToLower()))
                {
                    return;
                }

                // Read all lines from the current file
                string[] lines = File.ReadAllLines(filePaths.Item1);
                int lineNumber = 0;
                string relativePath = filePaths.Item1;

                //if the path of the file includes the project name
                if (filePaths.Item1.Contains(filePaths.Item2))
                {
                     relativePath = filePaths.Item1.Substring(filePaths.Item1.LastIndexOf("\\" + filePaths.Item2 + "\\")).TrimStart('\\');
                }
                else
                {
                    //if the path of the file doesn't include the project name just take last 3 folders in the path
                    var pathParts = filePaths.Item1.Split(Path.DirectorySeparatorChar);
                    var lastThreeFoldersWithFile = pathParts.Skip(Math.Max(0, pathParts.Length - 4)).ToArray();
                    relativePath=  string.Join(Path.DirectorySeparatorChar.ToString(), lastThreeFoldersWithFile);
                }

                // Search for the string in each line
                string xmlContent = string.Empty;
                foreach (string line in lines)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }
                    int index = line.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
                    if (index != -1)
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.Append(SecurityElement.Escape(relativePath))
                          .Append(BraceStart)
                          .Append((lineNumber + 1).ToString())
                          .Append(BraceEnd)
                          .Append(SecurityElement.Escape(line.Substring(0, index)))
                          .Append(RunStart)
                          .Append(SecurityElement.Escape(searchText))
                          .Append(RunEnd)
                          .Append(SecurityElement.Escape(line.Substring(index + searchText.Length)));

                        xmlContent = string.Format("<TextBlock xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TextWrapping=\"Wrap\">{0}</TextBlock>", sb.ToString());

                        Item listItem = new Item(filePaths.Item1, xmlContent, lineNumber, index);
                        list.Add(listItem);
                    }
                    lineNumber++;
                }
            }
            catch (Exception ex)
            {
                // Handle any exceptions that may occur while processing the file
                Console.WriteLine($"Error processing file '{filePaths.Item1}': {ex.Message}");
            }
        }

        private List<(string,string)> GetAllSolutionFiles(string searchText, CancellationToken token)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            List<(string,string)> matchList = new List<(string,string)>();
            DTE dte = null;
            string projectName = string.Empty;
            string projectPath = string.Empty;
            try
            {
                dte = (EnvDTE.DTE)Package.GetGlobalService(typeof(DTE));

                if (dte != null && dte.Solution != null)
                {
                    Solution solution = dte.Solution;

                    string solutionFilePath = dte.Solution.FullName;

                    foreach (Project project in solution.Projects)
                    {
                        // Recursively process projects
                        projectPath = project.FileName;
                        projectName = project.Name;
                        if (!String.IsNullOrEmpty(projectPath) && (!token.IsCancellationRequested))
                        {
                            SearchInProject(project, searchText, projectName, matchList,token);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle any exceptions that may occur during the process
                MessageBox.Show($"Error at Project level: {ex.Message}");
            }
            return matchList;
        }

        private void SearchInProject(Project project, string searchText, string projectName, List<(string,string)> matchList, CancellationToken token)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (project == null)
            {
                return;
            }

            foreach (ProjectItem item in project.ProjectItems)
            {
                if (item.Kind.Equals(Constants.vsProjectItemKindPhysicalFolder, StringComparison.OrdinalIgnoreCase)
                    || item.Kind.Equals(Constants.vsProjectItemKindVirtualFolder, StringComparison.OrdinalIgnoreCase))
                {
                    SearchInFolder(item, searchText, projectName, matchList,token);
                }
                else
                {
                    SearchInFile(item, searchText, projectName, matchList,token);
                }
            }
        }


        private void SearchInFolder(ProjectItem projectItem, string searchText, string projectName, List<(string,string)> matchList, CancellationToken token)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (projectItem == null)
            {
                return;
            }

            foreach (ProjectItem item in projectItem.ProjectItems)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }
                //if folder, call yourself
                if (item.Kind.Equals(Constants.vsProjectItemKindPhysicalFolder, StringComparison.OrdinalIgnoreCase)
                    || item.Kind.Equals(Constants.vsProjectItemKindVirtualFolder, StringComparison.OrdinalIgnoreCase))
                {
                    SearchInFolder(item, searchText, projectName, matchList, token);
                }
                else
                {
                    SearchInFile(item, searchText, projectName, matchList, token);
                }
            }
        }

        private void SearchInFile(ProjectItem item, string searchText, string projectName, List<(string,string)> matchList, CancellationToken token)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string filePath = item.FileNames[0];
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                if (CheckExtensionBanList(filePath))
                {
                    return;
                }

                //Check for WPF files
                string fileExtension = filePath.Substring(filePath.LastIndexOf('.'));
                if (string.Equals(fileExtension, ".xaml", StringComparison.OrdinalIgnoreCase))
                {
                    SearchInFolder(item, searchText, projectName, matchList, token);
                }

                matchList.Add((filePath, projectName));
            }
        }

        private bool CheckExtensionBanList(string file)
        {
            string ext = file.Substring(file.LastIndexOf("."));
            foreach (var word in banList)
            {
                if (ext.Equals(word))
                {
                    return true;
                }
            }
            return false;
        }

        private void MyListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                Item selectedItem = myListView.SelectedItem as Item;

                if (selectedItem != null)
                {
                    string path = selectedItem.FullPath;
                    int line = selectedItem.Line;
                    int position = selectedItem.Position;

                    EnvDTE.DTE dte = (EnvDTE.DTE)Package.GetGlobalService(typeof(EnvDTE.DTE));
                    // Open the file in the Visual Studio editor
                    var window = dte.ItemOperations.OpenFile(path, EnvDTE.Constants.vsViewKindCode);
                    window.Activate();

                    // Get the text view for the active document
                    IVsTextView textView = GetActiveTextView();

                    Document activeDocument = dte.ActiveDocument;
                    EnvDTE.TextSelection selection = activeDocument.Selection as EnvDTE.TextSelection;

                    if (textView != null)
                    {
                        // Set the cursor to the specified line and column
                        textView.SetCaretPos(line, position);

                        // Perform the search
                        string searchText = inputBox.Text;
                        bool found = selection.FindText(searchText);

                        textView.CenterLines(line, 1);
                        window.SetFocus();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

        private IVsTextView GetActiveTextView()
        {
            IVsTextManager textManager = Package.GetGlobalService(typeof(SVsTextManager)) as IVsTextManager;
            IVsTextView activeView = null;

            textManager.GetActiveView(1, null, out activeView);

            return activeView;
        }

        public class Item
        {
            public string FullPath { get; set; }
            public string Content { get; set; }
            public int Line { get; set; }
            public int Position { get; set; }
            public Item(string FullPath, string Content, int Line, int position)
            {
                this.FullPath = FullPath;
                this.Content = Content;
                this.Line = Line;
                this.Position = position;
            }
        }
    }
}

public class MatchEventArgs : EventArgs
{
    public int Matches { get; }

    public MatchEventArgs(int matches)
    {
        Matches = matches;
    }
}