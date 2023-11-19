using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

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
        private readonly List<ListViewItem> emptyList = new List<ListViewItem>();
        private const int upperBoundLineNumber = 25;
        private readonly string[] banList = { ".filters", ".png", ".jpg", ".vsixmanifest" };
        private CancellationTokenSource cts = new CancellationTokenSource();

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
                    foreach( ListViewItem item in myListView.SelectedItems)
                    {
                        if(null != item)
                        {
                            item.Focus();
                        }
                    }

                    //ListViewItem item = myListView.SelectedItem as ListViewItem;
                            
                    ///myListView.Focus();
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
            cts.Cancel();
            cts = new CancellationTokenSource();
            var token = cts.Token;

            if (inputBox.Text.Length < 1)
            {
                myListView.ItemsSource = emptyList;
                RaiseMatchEvent(0);
                return;
            }
            string searchText = inputBox.Text;
            var filesList = GetAllMatchFiles(searchText);


            List<Item> matchList = await Task.Run(()=>GetAllMatchInfo(filesList, searchText,token));
            List<ListViewItem> viewItems = await ConvertToListViewItems(matchList,token);

            myListView.ItemsSource = viewItems;
            RaiseMatchEvent(matchList.Count);
        }

        private async Task<List<ListViewItem>> ConvertToListViewItems(List<Item> matchList, CancellationToken token)
        {
            List<ListViewItem> list = new List<ListViewItem>();

            const int batchSize = 20;
            int count = 0;
            foreach(Item item in matchList)
            {
                if (token.IsCancellationRequested)
                {
                    return list;
                }

                if(count >= batchSize)
                {
                    count = 0;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        myListView.ItemsSource = list;
                    },DispatcherPriority.Background);
                   await Task.Delay(10);
                }
                Application.Current.Dispatcher.Invoke(() =>
                {
                        ListViewItem listViewItem = new ListViewItem();
                        listViewItem.Content = item.Content;
                        listViewItem.Tag = item;
                        list.Add(listViewItem);
                },DispatcherPriority.Background);
                count++;
            }

            return list;
        }

        private List<Item> GetAllMatchInfo(List<(string,string)> filesList, string searchText, CancellationToken token)
        {
            List<Item> list = new List<Item>();
            foreach(var filePath in filesList)
            {
                if (!token.IsCancellationRequested)
                {
                    SearchInLines(filePath, list, searchText);
                }
            }
            return list;
        }

        private void SearchInLines((string,string) filePath, List<Item> list, string searchText)
        {
             try
                {
                    // Read all lines from the current file
                    string fileContent = File.ReadAllText(filePath.Item1);

                    string[] lines = File.ReadAllLines(filePath.Item1);
                    int lineNumber = 0;
                    string relativePath = filePath.Item1.Substring(filePath.Item1.LastIndexOf("\\"+filePath.Item2+"\\")).TrimStart('\\');
                    string content = string.Empty;

                    // Search for the string in each line
                    foreach (string line in lines)
                    {
                        int index = line.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
                        if (index != -1)
                        {
                            // If the line contains the search string, add it to the matchingLines list
                            content = relativePath + " (" + (lineNumber + 1).ToString() + ")         " + line.TrimStart();

                            Item listItem = new Item(filePath.Item1, content, lineNumber, index);
                           // ListViewItem listViewItem = new ListViewItem();
                            //listViewItem.Content = content;
                            //listViewItem.Tag = listItem;
                            list.Add(listItem);
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

        private List<(string,string)> GetAllMatchFiles(string searchText)
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

        private void SearchInProject(Project project, string searchText, string projectName, List<(string,string)> matchList)
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


        private void SearchInFolder(ProjectItem projectItem, string searchText, string projectName, List<(string,string)> matchList)
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

        private void SearchInFile(ProjectItem item, string searchText, string projectName, List<(string,string)> matchList)
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

                    matchList.Add((filePath, projectName));

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
                ListViewItem selectedListViewItem = myListView.SelectedItem as ListViewItem;
                if(null == selectedListViewItem)
                {
                    return;
                }

                Item selectedItem = selectedListViewItem.Tag as Item;
                if(null == selectedItem)
                {
                    return;
                }

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