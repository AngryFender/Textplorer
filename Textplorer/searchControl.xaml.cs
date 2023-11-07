﻿using EnvDTE;
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

            //this.IsVisibleChanged += VisibleChangedHandler;
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
            string root = "";
            try
            {
                dte = (EnvDTE.DTE)Package.GetGlobalService(typeof(DTE));

                if (dte != null && dte.Solution != null)
                {
                    Solution solution = dte.Solution;

                    string solutionFilePath = dte.Solution.FullName;
                    root = Path.GetDirectoryName(solutionFilePath);

                    foreach (Project project in solution.Projects)
                    {
                        // Recursively process projects
                        string name = project.FileName;
                        if (!String.IsNullOrEmpty(name)) 
                        { 
                            SearchInProject(project, searchText,root, matchList);
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

        private void SearchInProject(Project project, string searchText, string root, List<Item> matchList)
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
                    SearchInFolder(item, searchText,root, matchList);
                }
                else
                {
                    SearchInFile(item, searchText,root, matchList);
                }
            }
        }


        private void SearchInFolder(ProjectItem projectItem, string searchText, string root, List<Item> matchList)
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
                    SearchInFolder(item, searchText,root, matchList);
                }
                else
                {
                    SearchInFile(item, searchText, root, matchList);
                }
            }
        }

        private void SearchInFile(ProjectItem item, string searchText, string root, List<Item> matchList)
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
                    int maxLine = lines.Length;

                    // Search for the string in each line
                    foreach (string line in lines)
                    {
                        string relativePath = filePath.Replace(root, "");

                        int index = line.IndexOf(searchText,StringComparison.OrdinalIgnoreCase);
                        if (index != -1)
                        { 
                            // If the line contains the search string, add it to the matchingLines list
                            Item listItem = new Item(filePath,relativePath+" ("+(lineNumber+1).ToString()+")", line, lineNumber, maxLine, index, line.Length);
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


        private  void MyListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                Item selectedItem = myListView.SelectedItem as Item;

                if (selectedItem != null)
                {
                    string path = selectedItem.FullPath;
                    int line = selectedItem.Line;
                    int maxLine = selectedItem.MaxLine;
                    int position = selectedItem.Position;
                    int endPosition = selectedItem.EndPosition;

                    EnvDTE.DTE dte = (EnvDTE.DTE)Package.GetGlobalService(typeof(EnvDTE.DTE));
                    // Open the file in the Visual Studio editor
                    dte.ItemOperations.OpenFile(path, EnvDTE.Constants.vsViewKindPrimary);

                    // Get the text view for the active document
                    IVsTextView textView = GetActiveTextView();

                    Document activeDocument = dte.ActiveDocument;
                    EnvDTE.TextSelection selection = activeDocument.Selection as EnvDTE.TextSelection;

                    if (textView != null)
                    {
                        // Set the cursor to the specified line and column
                        textView.SetCaretPos(line, position);
                        TextSpan ts;
                        ProcesTextSpan(line, maxLine, out ts);

                        // Perform the search
                        string searchText = inputBox.Text;
                        bool found = selection.FindText(searchText);

                        textView.EnsureSpanVisible(ts);
                    }
                }
            }
            catch(Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

        private void ProcesTextSpan(int line, int maxLine,out TextSpan ts)
        {
            ts = new TextSpan();

            int start = line - upperBoundLineNumber;
            if(start < 0)
            {
                ts.iStartLine = 0;
            }
            else
            {
                ts.iStartLine = start;
            }
            
            int end = line + upperBoundLineNumber;
            if(end > maxLine)
            {
                ts.iEndLine = maxLine;
            }
            else
            {
                ts.iEndLine = end;
            }
        }

        private IVsTextView GetActiveTextView()
        {
            IVsTextManager textManager = Package.GetGlobalService(typeof(SVsTextManager)) as IVsTextManager;
            IVsTextView activeView = null;

            textManager.GetActiveView(1, null, out activeView);

            return activeView;
        }

        public static List<string> GetAllFilenamesInSolution(out string rootPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            List<string> filenames = new List<string>();
            DTE dte = null;
            string root = "";
            try
            {
                dte = (EnvDTE.DTE)Package.GetGlobalService(typeof(DTE));

                if (dte != null && dte.Solution != null)
                {
                    Solution solution = dte.Solution;

                    string solutionFilePath = dte.Solution.FullName;
                    root = Path.GetDirectoryName(solutionFilePath);

                    foreach (Project project in solution.Projects)
                    {
                        // Recursively process projects
                        string name = project.FileName;
                        if (!String.IsNullOrEmpty(name)) 
                        { 
                            TraverseProject(project, filenames);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle any exceptions that may occur during the process
                MessageBox.Show($"Error at Project level: {ex.Message}");
            }

            rootPath = root;
            return filenames;
        }

        private static void TraverseProject(Project project, List<string> filenames)
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
                    TraverseProjectItem(item, filenames);
                }
                else
                {
                    ProcessFileName(item, filenames);
                }
            }
        }

        private static void TraverseProjectItem(ProjectItem projectItem, List<string> filenames)
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
                    TraverseProjectItem(item, filenames);
                }
                else
                {
                    ProcessFileName(item, filenames);
                }
            }
        }

        private static void ProcessFileName(ProjectItem item, List<string> filenames)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string filePath = item.FileNames[0];
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                string fileExtension = filePath.Substring(filePath.LastIndexOf('.'));
                if (string.Equals(fileExtension, ".filters", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                filenames.Add(filePath);
            }
        }

        public static List<Item> SearchForStringInFiles(List<string> filenames, string searchString, string rootPath)
        {
            var results = new List<Item>();

            foreach (string fullPath in filenames)
            {
                try
                {
                    // Read all lines from the current file
                    string fileContent = File.ReadAllText(fullPath);
                    if (!fileContent.Contains(searchString))
                    {
                        continue;
                    }

                    string[] lines = File.ReadAllLines(fullPath);
                    int lineNumber = 0;

                    // Search for the string in each line
                    int maxLine = lines.Length;
                    foreach (string line in lines)
                    {
                        string relativePath = fullPath.Replace(rootPath, "");

                        int index = line.IndexOf(searchString,StringComparison.OrdinalIgnoreCase);
                        if (index != -1)
                        { 
                            // If the line contains the search string, add it to the matchingLines list
                            Item item = new Item(fullPath,relativePath+" ("+(lineNumber+1).ToString()+")", line, maxLine, lineNumber,index, line.Length);
                            results.Add(item);
                        }
                        lineNumber++;
                    }
                }
                catch (Exception ex)
                {
                    // Handle any exceptions that may occur while processing the file
                    Console.WriteLine($"Error processing file '{fullPath}': {ex.Message}");
                }
            }

            return results;
        }

        public class Item
        {
            public string FullPath { get; set; }
            public string RelativePath { get; set; }
            public string Content { get; set; }
            public int Line { get; set; }
            public int MaxLine { get; set; }
            public int Position { get; set; }
            public int EndPosition { get; set; }
            public Item(string FullPath, string RelativePath, string Content, int Line, int maxLine, int position, int endPosition)
            {
                this.FullPath = FullPath;
                this.RelativePath = RelativePath;
                this.Content = Content;
                this.Line = Line;
                this.MaxLine = maxLine;
                this.Position = position;
                this.EndPosition = endPosition;
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