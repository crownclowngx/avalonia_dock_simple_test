using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MyAvaloniaManagement.Business.Helpers;

namespace MyAvaloniaManagement.Models.Tools;

internal sealed class CategoryNode : INotifyPropertyChanged
{
    private bool _isExpanded = false;

    public string CategoryName { get; set; }
    public List<DocumentCreationMenuEntry> Documents { get; set; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    public CategoryNode(string categoryName, List<DocumentCreationMenuEntry> documents)
    {
        CategoryName = categoryName;
        Documents = documents;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
