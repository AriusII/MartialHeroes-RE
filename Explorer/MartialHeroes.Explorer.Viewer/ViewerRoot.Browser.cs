using Godot;

namespace MartialHeroes.Explorer.Viewer;

public partial class ViewerRoot
{
    private void BuildNavigationTree()
    {
        if (_navigationTree is null || _browser is null) return;
        _navigationTree.Clear();

        var root = _navigationTree.CreateItem();

        var mapsItem = _navigationTree.CreateItem(root);
        mapsItem.SetText(0, "Maps");
        mapsItem.SetMeta("type", "category");
        mapsItem.Collapsed = true;
        var mapIds = MapAssembler.DiscoverMapIds(_browser.Archive);
        foreach (var id in mapIds)
        {
            var leaf = _navigationTree.CreateItem(mapsItem);
            leaf.SetText(0, id);
            leaf.SetMeta("type", "map");
            leaf.SetMeta("id", id);
        }

        GD.Print($"[Viewer] Tree: {mapIds.Count} maps populated.");

        var charsItem = _navigationTree.CreateItem(root);
        charsItem.SetText(0, "Personnages");
        charsItem.SetMeta("type", "category");
        charsItem.Collapsed = false;
        foreach (var (cls, name) in CharacterAssembler.Classes)
        {
            var clsItem = _navigationTree.CreateItem(charsItem);
            clsItem.SetText(0, $"{cls} {name}");
            clsItem.SetMeta("type", "category");
            clsItem.Collapsed = true;
            for (var v = 0; v <= 3; v++)
            {
                var varLeaf = _navigationTree.CreateItem(clsItem);
                varLeaf.SetText(0, $"Variante {v}");
                varLeaf.SetMeta("type", "char");
                varLeaf.SetMeta("cls", cls);
                varLeaf.SetMeta("variant", v);
            }
        }

        foreach (var family in _browser.Families)
        {
            var files = _browser.GetFiles(family);
            var famItem = _navigationTree.CreateItem(root);
            famItem.SetText(0, family);
            famItem.SetMeta("type", "category");
            famItem.Collapsed = true;
            foreach (var path in files)
            {
                var leaf = _navigationTree.CreateItem(famItem);
                leaf.SetText(0, path);
                leaf.SetMeta("type", "file");
                leaf.SetMeta("path", path);
            }
        }

        _navigationTree.ItemSelected += OnTreeItemSelected;
        _searchEdit!.TextChanged += FilterTree;
        PopulateFileCount(_navigationTree);
    }

    private void OnTreeItemSelected()
    {
        var item = _navigationTree?.GetSelected();
        if (item is null) return;
        if (!item.HasMeta("type")) return;
        var type = item.GetMeta("type").AsString();

        switch (type)
        {
            case "map":
            {
                var id = item.GetMeta("id").AsString();
                var key = "map:" + id;
                if (key == _currentSelectionKey) return;
                ClearPreview(EvictionPolicy.Full);
                _currentSelectionKey = key;
                BuildMap(id);
                break;
            }
            case "char":
            {
                var cls = item.GetMeta("cls").AsInt32();
                var variant = item.GetMeta("variant").AsInt32();
                var key = $"char:{cls}:{variant}";
                if (key == _currentSelectionKey) return;
                var policy = _currentIsHeavy ? EvictionPolicy.Full : EvictionPolicy.Targeted;
                ClearPreview(policy);
                _currentSelectionKey = key;
                AssembleAvatar(cls, variant);
                break;
            }
            case "file":
            {
                var path = item.GetMeta("path").AsString();
                var key = "file:" + path;
                if (key == _currentSelectionKey) return;
                var policy = _currentIsHeavy ? EvictionPolicy.Full : EvictionPolicy.Targeted;
                ClearPreview(policy);
                _currentSelectionKey = key;
                PreviewPath(path);
                break;
            }
        }
    }

    private void FilterTree(string filter)
    {
        if (_navigationTree is null) return;
        var root = _navigationTree.GetRoot();
        if (root is null) return;
        var visibleLeaves = 0;
        var catItem = root.GetFirstChild();
        while (catItem is not null)
        {
            var leafItem = catItem.GetFirstChild();
            var catVisible = false;
            while (leafItem is not null)
            {
                var leafType = leafItem.HasMeta("type") ? leafItem.GetMeta("type").AsString() : "category";
                if (leafType == "category")
                {
                    var subLeaf = leafItem.GetFirstChild();
                    var subVisible = false;
                    while (subLeaf is not null)
                    {
                        var show = filter.Length == 0 ||
                                   subLeaf.GetText(0).Contains(filter, StringComparison.OrdinalIgnoreCase);
                        subLeaf.Visible = show;
                        if (show)
                        {
                            subVisible = true;
                            visibleLeaves++;
                        }

                        subLeaf = subLeaf.GetNext();
                    }

                    leafItem.Visible = subVisible;
                    if (subVisible && filter.Length > 0) leafItem.Collapsed = false;
                    if (subVisible) catVisible = true;
                }
                else
                {
                    var show = filter.Length == 0 ||
                               leafItem.GetText(0).Contains(filter, StringComparison.OrdinalIgnoreCase);
                    leafItem.Visible = show;
                    if (show)
                    {
                        catVisible = true;
                        visibleLeaves++;
                    }
                }

                leafItem = leafItem.GetNext();
            }

            catItem.Visible = catVisible;
            if (catVisible && filter.Length > 0) catItem.Collapsed = false;
            catItem = catItem.GetNext();
        }

        PopulateFileCount(visibleLeaves);
    }

    private void PopulateFileCount(Tree tree)
    {
        var count = 0;
        var root = tree.GetRoot();
        if (root is null)
        {
            PopulateFileCount(0);
            return;
        }

        var cat = root.GetFirstChild();
        while (cat is not null)
        {
            var leaf = cat.GetFirstChild();
            while (leaf is not null)
            {
                var leafType = leaf.HasMeta("type") ? leaf.GetMeta("type").AsString() : "category";
                if (leafType == "category")
                {
                    var sub = leaf.GetFirstChild();
                    while (sub is not null)
                    {
                        count++;
                        sub = sub.GetNext();
                    }
                }
                else
                {
                    count++;
                }

                leaf = leaf.GetNext();
            }

            cat = cat.GetNext();
        }

        PopulateFileCount(count);
    }

    private void PopulateFileCount(int count)
    {
        if (_fileCountLabel is not null)
            _fileCountLabel.Text = $"{count} entrées";
    }
}