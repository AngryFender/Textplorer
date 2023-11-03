using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using EnvDTE;
using System;
using System.IO;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;

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
        public searchControl()
        {
            this.InitializeComponent();

            //this.IsVisibleChanged += VisibleChangedHandler;
            this.IsVisibleChanged += VisibleChangedHandler;
            inputBox.TextChanged += InputBox_TextChanged;
            myListView.SelectionChanged += MyListView_SelectionChanged;
        }

        private void VisibleChangedHandler(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.IsVisible)
            {
                inputBox.SelectAll();
                inputBox.Focus();
            }
        }


        private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            List<Item> items = new List<Item>();
            if (inputBox.Text.Length<2)
            {
                var lists = new List<Item>();
                myListView.ItemsSource = lists;
                return;
            }

            string rootPath;
            var nameLists = GetAllFilenamesInSolution(out rootPath);
            var resultLists = SearchForStringInFiles(nameLists, inputBox.Text, rootPath);


            foreach (var item in resultLists)
            {
                items.Add(item);
            }

            myListView.ItemsSource = items;
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
                    dte.ItemOperations.OpenFile(path, EnvDTE.Constants.vsViewKindPrimary);

                    // Get the text view for the active document
                    IVsTextView textView = GetActiveTextView();

                    Document activeDocument = dte.ActiveDocument;
                    TextSelection selection = activeDocument.Selection as TextSelection;

                    if (textView != null)
                    {
                        // Set the cursor to the specified line and column
                        textView.SetCaretPos(line, position);

                        string searchText = inputBox.Text;

                        // Perform the search
                        bool found = selection.FindText(searchText, (int)vsFindOptions.vsFindOptionsMatchCase);

                        if (found)
                        {
                            int startLine = selection.AnchorPoint.Line;
                            int startColumn = selection.AnchorPoint.LineCharOffset;

                            int endLine = selection.ActivePoint.Line;
                            int endColumn = selection.ActivePoint.LineCharOffset;
                        }
                    }
                }
            }
            catch(Exception ex)
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
                    AddFileName(item, filenames);
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
                    AddFileName(item, filenames);
                }
            }
        }

        private static void AddFileName(ProjectItem item, List<string> filenames)
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
                    string[] lines = File.ReadAllLines(fullPath);

                    int lineNumber = 0;

                    // Search for the string in each line
                    foreach (string line in lines)
                    {
                        string relativePath = fullPath.Replace(rootPath, "");

                        int index = line.IndexOf(searchString,StringComparison.OrdinalIgnoreCase);
                        if (index != -1)
                        { 
                            // If the line contains the search string, add it to the matchingLines list
                            Item item = new Item(fullPath,relativePath+" ("+(lineNumber+1).ToString()+")", line, lineNumber,index);
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
            public int Position { get; set; }
            public Item(string FullPath, string RelativePath, string Content, int Line, int position)
            {
                this.FullPath = FullPath;
                this.RelativePath = RelativePath;
                this.Content = Content;
                this.Line = Line;
                this.Position = position;
            }
        }
    }
}