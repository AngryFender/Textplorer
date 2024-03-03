using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
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
        private readonly string[] banList = { ".filters", ".png", ".jpg", ".vsixmanifest",".dll", ".ico" };
        private List<Project> projectList = new List<Project>();
        private CancellationTokenSource cts = new CancellationTokenSource();
        public static StringToXamlConverter xamlConverter = new StringToXamlConverter();
        private CheckBox selectAllCheckBox;
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
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!this.IsVisible)
            {
                return;
            }
                
            List<string> projectNames = GetAllProjectnames(projectList);
            UpdateCheckboxes(projectNames);

            if (inputBox.IsKeyboardFocused)
            {
                EnvDTE.DTE dte = (EnvDTE.DTE)Package.GetGlobalService(typeof(EnvDTE.DTE));
                IVsTextView textView = GetActiveTextView();
                string selectedWord;
                textView.GetSelectedText(out selectedWord);
                if (!String.IsNullOrEmpty(selectedWord) && inputBox.Text.ToLower() != selectedWord.ToLower())
                {
                    inputBox.Text = selectedWord;
                }

                inputBox.SelectAll();
                inputBox.Focus();
            }
        }

        private List<string> GetAllProjectnames(List<Project> projectList)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            List<string> projectNames = new List<string>();
            projectList.Clear();
            try
            {
                DTE dte = (EnvDTE.DTE)Package.GetGlobalService(typeof(DTE));
                if (dte != null && dte.Solution != null)
                {
                    string solutionFilePath = dte.Solution.FullName;
                    foreach (Project project in dte.Solution.Projects)
                    {
                        string projectPath = project.FileName;
                        string projectName = project.Name;
                        if (!String.IsNullOrEmpty(projectPath)) 
                        {
                            projectList.Add(project);
                            projectNames.Add(projectName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle any exceptions that may occur during the process
                MessageBox.Show($"Error at Project level: {ex.Message}");
            }
            return projectNames;
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
                        if(tinyList.Count() > 1000) {
                            Dispatcher.Invoke(() => SetListViewSource(tinyList));
                        }
                    });
                
                List<Item> aList = new List<Item>();
                List<Item> bList = new List<Item>();
                List<Item> cList = new List<Item>();
                List<Item> dList = new List<Item>();

                var firstList = new List<(string,string)>();
                var secondList = new List<(string,string)>();
                var thirdList = new List<(string,string)>();
                var fourthList = new List<(string,string)>();

                int divValue = matchFiles.Count / 4;

                int count = 0;
                foreach(var file in matchFiles)
                {
                    if (count < divValue)
                    {
                        firstList.Add(file);
                    }
                    else if (count >= divValue && count < (2 * divValue))
                    {
                        secondList.Add(file);
                    }
                    else if (count >= (2 * divValue) && count < (3 * divValue))
                    {
                        thirdList.Add(file);
                    }
                    else
                    {
                        fourthList.Add(file);
                    }
                    count++;
                }

                var firstTask = Task.Run(() => GetAllMatchingItems(aList, firstList, searchText, token));
                var secondTask = Task.Run(() => GetAllMatchingItems(bList, secondList, searchText, token));
                var thirdTask = Task.Run(() => GetAllMatchingItems(cList, thirdList, searchText, token));
                var fourthTask = Task.Run(() => GetAllMatchingItems(dList, fourthList, searchText, token));

                await Task.WhenAll(tinyTask,firstTask, secondTask,thirdTask,fourthTask);

                var result = aList.Concat(bList).Concat(cList).Concat(dList).ToList();

                SetListViewSource(result);

                FilterListView();
            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void UpdateCheckboxes(List<string> projectList)
        {
            List<string> remainList = new List<string>(projectList);
            remainList.Insert(0,"All");

            foreach (var checkBox in checkBoxPanel.Children.OfType<CheckBox>().ToList())
            {
                if (!remainList.Remove(checkBox.Content.ToString()))
                {
                    checkBox.Checked -= CheckBoxClicked;
                    checkBox.Unchecked -= CheckBoxClicked;
                    checkBoxPanel.Children.Remove(checkBox);
                }
            }

            foreach(string name in remainList)
            {
                CheckBox checkBox = new CheckBox
                {
                    Content = name,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3D3D3D")),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFA0A0A0")),
                    IsChecked = true,
                    Padding = new Thickness(0, 0, 10, 0) 
                };
                checkBox.Checked += CheckBoxClicked;
                checkBox.Unchecked += CheckBoxClicked;
                checkBoxPanel.Children.Add(checkBox);

                if(name == "All")
                {
                    selectAllCheckBox = checkBox;
                }
            }
        }

        private void CheckBoxClicked(object sender, RoutedEventArgs e)
        {
            bool senderSelectAllCheckBox = false;
            if(selectAllCheckBox == sender as CheckBox)
            {
                senderSelectAllCheckBox = true;
            }

            FilterListView(senderSelectAllCheckBox);
        }

        private void FilterListView(bool senderSelectAllCheckBox = false)
        {
            bool isAnyCheckBoxUnchecked = false;
            List<string> projectNames = new List<string>();

            if ((bool)selectAllCheckBox.IsChecked && senderSelectAllCheckBox)
            {
                foreach (var checkBox in checkBoxPanel.Children.OfType<CheckBox>().ToList())
                {
                    checkBox.Checked -= CheckBoxClicked;
                    checkBox.IsChecked = true;
                    checkBox.Checked += CheckBoxClicked;
                    projectNames.Add(checkBox.Content.ToString());
                }
            }
            else
            {
                foreach (var checkBox in checkBoxPanel.Children.OfType<CheckBox>().ToList())
                {
                    if (selectAllCheckBox != checkBox)
                    {
                        if ((bool)checkBox.IsChecked)
                        {
                            projectNames.Add(checkBox.Content.ToString());
                        }
                        else
                        {
                            isAnyCheckBoxUnchecked = true;
                        }
                    }
                }
            }

            if (isAnyCheckBoxUnchecked && ! senderSelectAllCheckBox && (bool)selectAllCheckBox.IsChecked)
            { 
                selectAllCheckBox.Unchecked -= CheckBoxClicked;
                selectAllCheckBox.IsChecked = false;
                selectAllCheckBox.Unchecked += CheckBoxClicked;
            }

            if (!(myListView.ItemsSource is List<Item> matchList) || (matchList.Count == 0))
            {
                RaiseMatchEvent(0);
            }

            bool selectAll = false;
            if ((bool)selectAllCheckBox.IsChecked)
            {
                selectAll = true;
            }

            int count = 0;
            ICollectionView collectionView = CollectionViewSource.GetDefaultView(myListView.ItemsSource);
            collectionView.Filter = new Predicate<object>(rowItem  =>
            {
                if (selectAll)
                {
                    count++;
                    return true;
                }

                if (!(rowItem is Item item))
                {
                    return false;
                }

                foreach (var name in projectNames)
                {
                    if (item.Project == name)
                    {
                        count++;
                        return true;
                    }
                }
                return false;
            });
            RaiseMatchEvent(count);
        }

        private void SetListViewSource(List<Item> list, bool showCount = false)
        {
            if(inputBox.Text.Length < 1)
            {
                myListView.ItemsSource = emptyList;
            }
            else
            {
                myListView.ItemsSource= list;
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

                        Item listItem = new Item(filePaths.Item1, filePaths.Item2, xmlContent, lineNumber, index);
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

                        Item listItem = new Item(filePaths.Item1, filePaths.Item2, xmlContent, lineNumber, index);
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

        private List<(string,string)> GetAllSolutionFiles( string searchText, CancellationToken token)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            List<(string,string)> matchList = new List<(string,string)>();
            try
            {
                foreach (var project in projectList)
                {
                    // Recursively process projects
                    string projectPath = project.FileName;
                    string projectName = project.Name;
                    if (!String.IsNullOrEmpty(projectPath) && (!token.IsCancellationRequested))
                    {
                        SearchInProject(project, searchText, project.Name, matchList, token);
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
                    }

                    myListView.Focus();
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
            public string Project { get; set; }
            public int Line { get; set; }
            public int Position { get; set; }
            public Item(string FullPath, string Project, string Content, int Line, int position)
            {
                this.FullPath = FullPath;
                this.Content = Content;
                this.Project = Project;
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