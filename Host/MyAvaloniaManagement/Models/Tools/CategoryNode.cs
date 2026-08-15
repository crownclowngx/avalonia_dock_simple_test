using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MyAvaloniaManagementCommon.DocumentCreation;

namespace MyAvaloniaManagement.Models.Tools;

internal class CategoryNode : INotifyPropertyChanged
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
    
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
