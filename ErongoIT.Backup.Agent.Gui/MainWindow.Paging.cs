using System.Windows;
using System.Windows.Controls;

namespace ErongoIT.Backup.Agent.Gui;

/// <summary>
/// Page-by-page display of the Restore file list and the History list.
/// The number of lines per page is chosen under Settings > Display.
/// </summary>
public partial class MainWindow
{
    private readonly GuiSettings _guiSettings = GuiSettings.Load();

    private readonly PagedList<RestoreRow> _restorePages = new();
    private readonly PagedList<HistoryRow> _historyPages = new();

    private bool _pagingReady;

    private void InitializePaging()
    {
        RowsPerPageComboBox.ItemsSource = GuiSettings.RowsPerPageChoices;
        RowsPerPageComboBox.SelectedItem = _guiSettings.RowsPerPage;

        _restorePages.SetPageSize(_guiSettings.RowsPerPage);
        _historyPages.SetPageSize(_guiSettings.RowsPerPage);

        _pagingReady = true;

        ShowRestorePage();
        ShowHistoryPage();
    }

    // ------------------------------------------------------------
    // Settings
    // ------------------------------------------------------------

    private void RowsPerPageComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_pagingReady || RowsPerPageComboBox.SelectedItem is not int rows)
            return;

        _guiSettings.RowsPerPage = rows;
        _guiSettings.Save();

        _restorePages.SetPageSize(rows);
        _historyPages.SetPageSize(rows);

        ShowRestorePage();
        ShowHistoryPage();
    }

    // ------------------------------------------------------------
    // Restore
    // ------------------------------------------------------------

    private void SetRestoreRows(IReadOnlyList<RestoreRow> rows)
    {
        _restorePages.SetItems(rows);
        ShowRestorePage();
    }

    private void RestorePage_Click(
        object sender,
        RoutedEventArgs e)
    {
        _restorePages.Move((sender as Button)?.Tag as string);
        ShowRestorePage();
    }

    private void ShowRestorePage()
    {
        if (!_pagingReady)
            return;

        var page = _restorePages.CurrentPage;

        ApplySelectionState(page);

        RestoreFilesGrid.ItemsSource = page;

        if (page.Count > 0)
            RestoreFilesGrid.ScrollIntoView(page[0]);

        UpdatePager(
            _restorePages.Describe("items"),
            _restorePages,
            RestorePageInfo,
            RestorePageNumberText,
            RestoreFirstPageButton,
            RestorePreviousPageButton,
            RestoreNextPageButton,
            RestoreLastPageButton);
    }

    // ------------------------------------------------------------
    // History
    // ------------------------------------------------------------

    private void SetHistoryRows(IReadOnlyList<HistoryRow> rows, bool keepPage = false)
    {
        _historyPages.SetItems(rows, keepPage);
        ShowHistoryPage();
    }

    private void HistoryPage_Click(
        object sender,
        RoutedEventArgs e)
    {
        _historyPages.Move((sender as Button)?.Tag as string);
        ShowHistoryPage();
    }

    private void ShowHistoryPage()
    {
        if (!_pagingReady)
            return;

        HistoryGrid.ItemsSource = _historyPages.CurrentPage;

        UpdatePager(
            _historyPages.Describe("backups"),
            _historyPages,
            HistoryPageInfo,
            HistoryPageNumberText,
            HistoryFirstPageButton,
            HistoryPreviousPageButton,
            HistoryNextPageButton,
            HistoryLastPageButton);
    }

    // ------------------------------------------------------------

    private static void UpdatePager<T>(
        string info,
        PagedList<T> pages,
        TextBlock infoText,
        TextBlock pageNumberText,
        Button first,
        Button previous,
        Button next,
        Button last)
    {
        infoText.Text = info;
        pageNumberText.Text = $"Page {pages.PageIndex + 1:N0} of {pages.PageCount:N0}";

        var notFirst = pages.PageIndex > 0;
        var notLast = pages.PageIndex < pages.PageCount - 1;

        first.IsEnabled = notFirst;
        previous.IsEnabled = notFirst;
        next.IsEnabled = notLast;
        last.IsEnabled = notLast;
    }
}
