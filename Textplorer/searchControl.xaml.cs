using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using EnvDTE;
using EnvDTE80;
using System;
using System.Collections.Generic;
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
           


            inputBox.TextChanged += InputBox_TextChanged;
            myListView.SelectionChanged += MyListView_SelectionChanged; 



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
            var nameLists = GetAllFilenamesInSolution();
            var resultLists = SearchForStringInFiles(nameLists, inputBox.Text);


            foreach (var item in resultLists)
            {
                items.Add(item);
            }

            myListView.ItemsSource = items;
        }

        private void MyListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Item selectedItem = myListView.SelectedItem as Item;

            if (selectedItem != null)
            {
                string path = selectedItem.Name;
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
                        // Text was found; you can access selection.AnchorPoint and selection.ActivePoint
                        // to get the positions of the found text
                        int startLine = selection.AnchorPoint.Line;
                        int startColumn = selection.AnchorPoint.LineCharOffset;

                        int endLine = selection.ActivePoint.Line;
                        int endColumn = selection.ActivePoint.LineCharOffset;

                        // You can use these positions for further processing
                    }
                }
            }
        }
        private IVsTextView GetActiveTextView()
        {
            IVsTextManager textManager = Package.GetGlobalService(typeof(SVsTextManager)) as IVsTextManager;
            IVsTextView activeView = null;

            textManager.GetActiveView(1, null, out activeView);

            return activeView;
        }
        public static List<string> GetAllFilenamesInSolution()
        {
            List<string> filenames = new List<string>();
            DTE dte = null;
            try
            {
                dte = (DTE)System.Runtime.InteropServices.Marshal.GetActiveObject("VisualStudio.DTE"); // Adjust the version number as needed

                if (dte != null && dte.Solution != null)
                {
                    Solution solution = dte.Solution;
                    Console.WriteLine(solution.FullName);

                    foreach (Project project in solution.Projects)
                    {
                        // Recursively process projects
                        TraverseProject(project, filenames);
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle any exceptions that may occur during the process
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(dte);
            }

            return filenames;
        }



        private static void TraverseProject(Project project, List<string> filenames)
        {
            if (project == null)
            {
                return;
            }

            // Check if the project is a solution folder (a container for other projects)
            if (project.Kind == EnvDTE.Constants.vsProjectKindSolutionItems)
            {
                // Handle solution folders (if needed)
                // You can recursively explore the projects within the solution folder here.
                foreach (ProjectItem projectItem in project.ProjectItems)
                {
                    // Recursively process project items in solution folders
                    TraverseProjectItem(projectItem, filenames);
                }
            }
            else
            {
                // This project is not a solution folder; process its files
                ProcessProjectFiles(project, filenames);
            }
        }

        private static void TraverseProjectItem(ProjectItem projectItem, List<string> filenames)
        {
            if (projectItem == null)
            {
                return;
            }

            // Recursively explore project items within solution folders
            foreach (ProjectItem item in projectItem.ProjectItems)
            {
                TraverseProjectItem(item, filenames);
            }
        }

        private static void ProcessProjectFiles(Project project, List<string> filenames)
        {
            if (project == null)
            {
                return;
            }

            // Add the filenames of project files to the list
            foreach (ProjectItem item in project.ProjectItems)
            {
                // Get the full path to the project item (file)
                string filePath = item.FileNames[0];
                string fileName = item.Name;

                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    filenames.Add(filePath);
                }
            }
        }

        public static List<Item> SearchForStringInFiles(List<string> filenames, string searchString)
        {
            var results = new List<Item>();

            foreach (string filename in filenames)
            {
                try
                {
                    // Read all lines from the current file
                    string[] lines = File.ReadAllLines(filename);

                    int lineNumber = 0;

                    // Search for the string in each line
                    foreach (string line in lines)
                    {
                        int index = line.IndexOf(searchString,StringComparison.OrdinalIgnoreCase);

                        if (index != -1)
                        { 
                            // If the line contains the search string, add it to the matchingLines list
                            Item item = new Item(filename, line, lineNumber,index);
                            results.Add(item);
                        }
                        lineNumber++;
                    }
                }
                catch (Exception ex)
                {
                    // Handle any exceptions that may occur while processing the file
                    Console.WriteLine($"Error processing file '{filename}': {ex.Message}");
                }
            }

            return results;
        }

        /// <summary>
        /// Handles click on the button by displaying a message box.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event args.</param>
        [SuppressMessage("Microsoft.Globalization", "CA1300:SpecifyMessageBoxOptions", Justification = "Sample code")]
        [SuppressMessage("StyleCop.CSharp.NamingRules", "SA1300:ElementMustBeginWithUpperCaseLetter", Justification = "Default event handler naming pattern")]
        private void button1_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                string.Format(System.Globalization.CultureInfo.CurrentUICulture, "Invoked '{0}'", this.ToString()),
                "search");
        }
        public class Item
        {
            public string Name { get; set; }
            public string Content { get; set; }
            public int Line { get; set; }
            public int Position { get; set; }
            public Item(string Name, string Content, int Line, int position)
            {
                this.Name = Name;
                this.Content = Content;
                this.Line = Line;
                this.Position = position;
            }
        }
    }
}