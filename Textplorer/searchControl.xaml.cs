using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;

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
        private readonly string[] banList = { ".filters", ".png", ".jpg", ".vsixmanifest" };

        public searchControl()
        {
            this.InitializeComponent();

            inputBox.IsKeyboardFocusedChanged += VisibleChangedHandler;
            inputBox.TextChanged += InputBox_TextChanged;
            myListView.SelectionChanged += MyListView_SelectionChanged;
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

        private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (inputBox.Text.Length < 1)
            {
                myListView.ItemsSource = emptyList;
                RaiseMatchEvent(0);
                return;
            }

            var matchList = GetAllMatchingInfo(inputBox.Text);

            myListView.ItemsSource = matchList;
            RaiseMatchEvent(matchList.Count);
        }

        private List<Item> GetAllMatchingInfo(string searchText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            List<Item> matchList = new List<Item>();
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
                        if (!String.IsNullOrEmpty(projectPath))
                        {
                            SearchInProject(project, searchText, projectName, matchList);
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

        private void SearchInProject(Project project, string searchText, string projectName, List<Item> matchList)
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
                    SearchInFolder(item, searchText, projectName, matchList);
                }
                else
                {
                    SearchInFile(item, searchText, projectName, matchList);
                }
            }
        }


        private void SearchInFolder(ProjectItem projectItem, string searchText, string projectName, List<Item> matchList)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (projectItem == null)
            {
                return;
            }

            foreach (ProjectItem item in projectItem.ProjectItems)
            {
                //if folder, call yourself
                if (item.Kind.Equals(Constants.vsProjectItemKindPhysicalFolder, StringComparison.OrdinalIgnoreCase)
                    || item.Kind.Equals(Constants.vsProjectItemKindVirtualFolder, StringComparison.OrdinalIgnoreCase))
                {
                    SearchInFolder(item, searchText, projectName, matchList);
                }
                else
                {
                    SearchInFile(item, searchText, projectName, matchList);
                }
            }
        }

        private void SearchInFile(ProjectItem item, string searchText, string projectName, List<Item> matchList)
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
                    SearchInFolder(item, searchText, projectName, matchList);
                }

                try
                {
                    // Read all lines from the current file
                    string fileContent = File.ReadAllText(filePath);
                    if (!fileContent.ToLower().Contains(searchText.ToLower()))
                    {
                        return;
                    }

                    string[] lines = File.ReadAllLines(filePath);
                    int lineNumber = 0;
                    string relativePath = filePath.Substring(filePath.LastIndexOf("\\"+projectName+"\\")).TrimStart('\\');
                    string content = string.Empty;

                    // Search for the string in each line
                    foreach (string line in lines)
                    {
                        int index = line.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
                        if (index != -1)
                        {
                            // If the line contains the search string, add it to the matchingLines list
                            content = relativePath + " (" + (lineNumber + 1).ToString() + ")         " + line.TrimStart();

                            Item listItem = new Item(filePath, content, lineNumber, index);
                            matchList.Add(listItem);
                        }
                        lineNumber++;
                    }
                }
                catch (Exception ex)
                {
                    // Handle any exceptions that may occur while processing the file
                    Console.WriteLine($"Error processing file '{filePath}': {ex.Message}");
                }
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