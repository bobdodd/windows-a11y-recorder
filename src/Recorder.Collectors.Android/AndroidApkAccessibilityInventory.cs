using System.IO.Compression;
using System.Xml;
using Recorder.Contracts;

namespace Recorder.Collectors.Android;

/// <summary>Inspects every applicable package layer without assigning a framework to a screen.</summary>
internal sealed class AndroidApkAccessibilityInventory
{
    private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";
    private const int MaxDocumentBytes = 1024 * 1024;
    private const int MaxBinaryBytes = 16 * 1024 * 1024;
    private const int MaxDocuments = 1024;
    private const int MaxItems = 8192;
    private readonly List<AndroidApkAccessibilityItem> _items = [];
    private readonly Dictionary<string, (int Seen, int Read)> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _found = new(StringComparer.Ordinal);
    private readonly HashSet<string> _resourceReferences = new(StringComparer.Ordinal);
    private readonly List<(string Split, string Locator, string Id, string Value)> _resourceTargets = [];
    private bool _itemLimit;
    private int _documentsRead;

    private static readonly Dictionary<string, string> LayoutProperties = new(StringComparer.Ordinal)
    {
        ["textSize"] = "Typography", ["fontFamily"] = "Typography", ["textStyle"] = "Typography",
        ["textFontWeight"] = "Typography", ["textAppearance"] = "Typography", ["style"] = "Style reference",
        ["fontWeight"] = "Typography", ["fontStyle"] = "Typography", ["font"] = "Typography",
        ["autoSizeTextType"] = "Typography", ["autoSizeMinTextSize"] = "Typography", ["autoSizeMaxTextSize"] = "Typography",
        ["letterSpacing"] = "Typography", ["lineSpacingExtra"] = "Typography", ["lineSpacingMultiplier"] = "Typography",
        ["lineHeight"] = "Typography", ["maxLines"] = "Typography", ["ellipsize"] = "Typography",
        ["textColor"] = "Colour declaration", ["textColorHint"] = "Colour declaration",
        ["labelFor"] = "Form association", ["hint"] = "Form label or hint declaration", ["inputType"] = "Form input",
        ["autofillHints"] = "Autofill", ["importantForAutofill"] = "Autofill", ["imeOptions"] = "Form input",
        ["maxLength"] = "Form input", ["password"] = "Form input", ["error"] = "Form error declaration",
        ["contentDescription"] = "Accessible name declaration", ["tooltipText"] = "Tooltip declaration",
        ["stateDescription"] = "State declaration", ["accessibilityPaneTitle"] = "Pane title declaration",
        ["accessibilityHeading"] = "Heading", ["accessibilityLiveRegion"] = "Live region",
        ["accessibilityTraversalBefore"] = "Traversal", ["accessibilityTraversalAfter"] = "Traversal",
        ["focusable"] = "Focus", ["focusableInTouchMode"] = "Focus", ["screenReaderFocusable"] = "Focus",
        ["nextFocusUp"] = "Focus", ["nextFocusDown"] = "Focus", ["nextFocusLeft"] = "Focus",
        ["nextFocusRight"] = "Focus", ["nextFocusForward"] = "Focus", ["importantForAccessibility"] = "Semantics",
        ["minWidth"] = "Minimum dimensions", ["minHeight"] = "Minimum dimensions",
        ["visibility"] = "Visibility declaration", ["clickable"] = "Interaction declaration",
        ["longClickable"] = "Interaction declaration"
    };

    public void Inspect(
        ZipArchive archive, string split, IReadOnlyList<string> fonts, IReadOnlyList<AndroidApkFontFamily> families,
        bool flutterFontManifestRead, CancellationToken cancellation)
    {
        foreach (var font in fonts)
        {
            Add("bundled", "app", "Font file", split, font, font);
        }
        foreach (var family in families)
        {
            var declaration = $"{family.Family}; asset {family.Asset}" +
                (family.Weight is { } weight ? $"; weight {weight}" : "") +
                (family.Style is { } style ? $"; style {style}" : "");
            Add("declared", "app", "Font family", split, "assets/flutter_assets/FontManifest.json", declaration);
        }
        ZipArchiveEntry? resourceTable = null;
        foreach (var entry in archive.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            var name = entry.FullName;
            if (name == "assets/flutter_assets/FontManifest.json")
            {
                _found.Add("Flutter");
                Document("Flutter font manifest", flutterFontManifestRead);
            }
            if (name == "AndroidManifest.xml" || IsResourceXml(name, "layout") || IsResourceXml(name, "font"))
            {
                var reader = name == "AndroidManifest.xml" ? "Android manifest" : "Android layout and font XML";
                var manifest = name == "AndroidManifest.xml";
                ReadDocument(entry, reader, () => ReadLayout(entry, split, manifest), cancellation);
            }
            else if (name == "resources.arsc")
            {
                resourceTable ??= entry;
            }
            else if (name == "classes.dex" || name.StartsWith("classes", StringComparison.Ordinal) &&
                name.EndsWith(".dex", StringComparison.Ordinal) && name[7..^4].Length > 0 &&
                name[7..^4].All(char.IsAsciiDigit))
            {
                ReadDocument(entry, "Android DEX accessibility symbols", () => ReadDex(entry, split, cancellation),
                    cancellation, MaxBinaryBytes);
            }
            else if (IsNativeLibrary(name))
            {
                var library = name[(name.LastIndexOf('/') + 1)..];
                if (library is "libflutter.so" or "libreactnative.so" or "libreactnativejni.so" or
                    "libmonodroid.so" or "libunity.so" or "libil2cpp.so")
                {
                    _found.Add(library switch
                    {
                        "libflutter.so" => "Flutter",
                        "libreactnative.so" or "libreactnativejni.so" => "React Native",
                        _ => "Other compiled UI"
                    });
                    Add("bundled", "app", "Framework marker", split, name, library);
                }
                if (library == "libapp.so")
                {
                    _found.Add("Flutter code candidate");
                    Add("bundled", "app", "Compiled library candidate", split, name,
                        "libapp.so; contents not decoded");
                }
            }
            else if (name.StartsWith("META-INF/androidx.compose.", StringComparison.Ordinal) &&
                name.EndsWith(".version", StringComparison.Ordinal))
            {
                _found.Add("Jetpack Compose");
                Add("bundled", "app", "Framework marker", split, name, "AndroidX Compose package metadata");
            }
            else if (name == "assets/index.android.bundle" ||
                name.StartsWith("assets/", StringComparison.Ordinal) && name.EndsWith(".hbc", StringComparison.OrdinalIgnoreCase))
            {
                _found.Add("Android JavaScript bundle");
                InspectBundle(entry, split);
            }
            else if (name.StartsWith("assets/", StringComparison.Ordinal))
            {
                var extension = Path.GetExtension(name);
                switch (extension.ToLowerInvariant())
                {
                    case ".svg":
                        ReadDocument(entry, "Packaged SVG accessibility markup", () => ReadSvg(entry, split), cancellation);
                        break;
                    case ".html":
                    case ".htm":
                    case ".xhtml":
                        ReadDocument(entry, "Packaged HTML", () =>
                        {
                            using var markup = entry.Open();
                            AndroidPackagedHtml.Read(markup, (category, locator, value) =>
                                Add("declared", "component", category, split, $"{name} / {locator}", value), cancellation);
                        }, cancellation);
                        break;
                    case ".css":
                    case ".js":
                        _found.Add("Packaged web code");
                        break;
                }
            }
        }
        if (resourceTable is not null)
        {
            ReadDocument(resourceTable, "Android resource styles", () => ReadStyles(resourceTable, split, cancellation),
                cancellation, MaxBinaryBytes);
        }
    }

    private void InspectBundle(ZipArchiveEntry entry, string split)
    {
        var format = "Bundle present; payload format not established";
        try
        {
            using var bundle = entry.Open();
            Span<byte> header = stackalloc byte[8];
            if (bundle.ReadAtLeast(header, 8, throwOnEndOfStream: false) == 8 &&
                header.SequenceEqual(new byte[] { 0xc6, 0x1f, 0xbc, 0x03, 0xc1, 0x03, 0x19, 0x1f }))
            {
                format = "Hermes bytecode signature; code not decoded";
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            format = "Bundle present; header could not be read";
        }
        Add("bundled", "app", "JavaScript bundle candidate", split, entry.FullName, format);
    }

    public AndroidApkAccessibilityEvidence Build(int apksRead, int apksTotal)
    {
        foreach (var target in _resourceTargets)
        {
            if (_resourceReferences.Contains(target.Id))
            {
                Add("declared", "component", "Resource reference target", target.Split, target.Locator, target.Value);
            }
        }
        var coverage = _files.OrderBy(file => file.Key, StringComparer.Ordinal).Select(file =>
            new AndroidApkAccessibilityCoverage(file.Key, file.Value.Read == file.Value.Seen ? "read" : "partial",
                $"{file.Value.Read} of {file.Value.Seen} identified documents read.")).ToList();
        coverage.Add(new("APK inventory", apksRead == 0 ? "unavailable" : apksRead < apksTotal || _itemLimit ? "partial" : "read",
            $"{apksRead} of {apksTotal} installed APKs read." + (_itemLimit ? " Some declarations exceeded an item, field or resource limit." : "")));
        AddCoverage("Android resource resolution", _files.ContainsKey("Android resource styles"),
            "Direct style entries and referenced IDs, fonts, dimensions and accessibility strings are shown where recovered. " +
            "Inheritance, full reference chains and Android's active resource configuration were not calculated.");
        AddCoverage("Android DEX call paths", _files.ContainsKey("Android DEX accessibility symbols"),
            "Allowlisted method symbols identify API surface only. Call arguments, field construction, timers and semantics behavior were not reconstructed.");
        AddCoverage("Flutter compiled code", _found.Contains("Flutter") || _found.Contains("Flutter code candidate"),
            "Compiled Dart code was not decoded by this collector. Flutter font manifest declarations are reported separately.");
        AddCoverage("Jetpack Compose compiled code", _found.Contains("Jetpack Compose"),
            "Compose declaration, modifier and semantics code was not decoded.");
        AddCoverage("React Native / JavaScript", _found.Contains("React Native") || _found.Contains("Android JavaScript bundle"),
            "Bundled JavaScript or Hermes code was not decoded.");
        AddCoverage("Web CSS, scripts and live DOM", _files.ContainsKey("Packaged HTML") || _found.Contains("Packaged web code"),
            "Local HTML declarations are separate. CSS, scripts, remote content and the live DOM were not interpreted.");
        if (_found.Contains("Other compiled UI"))
        {
            AddCoverage("Other compiled UI", true, "Additional compiled UI markers were found; their code was not decoded.");
        }

        return new(1, _items.ToArray(), coverage);

        void AddCoverage(string reader, bool found, string detail)
        {
            coverage.Add(new(reader, found ? "not decoded" : apksRead == 0 ? "unavailable" : "not identified",
                found ? detail : "No supported input marker was identified in the APKs read; absence is not established."));
        }
    }

    private void ReadStyles(ZipArchiveEntry entry, string split, CancellationToken token)
    {
        var bytes = ReadBytes(entry);
        AndroidCompiledStyles.Read(bytes, (locator, property, value, isResourceReference) =>
        {
            if (property.StartsWith("reference:", StringComparison.Ordinal))
            {
                if (_resourceTargets.Count < MaxItems * 8 && locator.Length <= 768 && value.Length <= 1024)
                {
                    _resourceTargets.Add((split, locator, property[10..], value));
                }
                else
                {
                    _itemLimit = true;
                }
            }
            else
            {
                Add("declared", "component", LayoutProperties[property], split, locator, value);
                if (isResourceReference && _resourceReferences.Count < MaxItems)
                {
                    _resourceReferences.Add(value);
                }
            }
        }, _resourceReferences.Contains, token);
    }

    private void ReadDex(ZipArchiveEntry entry, string split, CancellationToken token)
    {
        AndroidDexAccessibilitySymbols.Read(ReadBytes(entry), (category, reference) =>
            Add("symbol", "app", category, split, entry.FullName, reference), token);
    }

    private void ReadLayout(ZipArchiveEntry entry, string split, bool manifest)
    {
        var bytes = ReadBytes(entry);
        var nodes = AndroidCompiledXml.Read(bytes, (ns, name) => manifest
            ? ns == AndroidNamespace && name is "screenOrientation" or "configChanges" or "windowSoftInputMode" or "supportsRtl"
            : ns is null && name == "style" ||
              ns == AndroidNamespace && name == "text" ||
              (ns == AndroidNamespace || ns == "http://schemas.android.com/apk/res-auto") && LayoutProperties.ContainsKey(name));
        foreach (var element in nodes)
        {
            var labelsAnotherView = !manifest && element.Attributes.Any(attribute =>
                attribute.Namespace == AndroidNamespace && attribute.Name == "labelFor") &&
                !element.Name.EndsWith("EditText", StringComparison.Ordinal) &&
                !element.Attributes.Any(attribute => attribute.Name == "inputType");
            foreach (var attribute in element.Attributes)
            {
                if (attribute.Name == "text" && !labelsAnotherView)
                {
                    continue;
                }
                var locator = $"{entry.FullName} / {element.Name} #{element.Ordinal}" +
                    (element.Id is null ? "" : $" ({element.Id})") + $" / {attribute.Name}";
                Add("declared", element.Name == "application" && manifest ? "app" : "component",
                    manifest ? "Manifest configuration" : attribute.Name == "text" ? "Form label declaration" :
                    LayoutProperties[attribute.Name], split, locator, attribute.Value);
                if (attribute.IsResourceReference && _resourceReferences.Count < MaxItems)
                {
                    _resourceReferences.Add(attribute.Value);
                }
            }
        }
    }

    private void ReadSvg(ZipArchiveEntry entry, string split)
    {
        using var content = entry.Open();
        using var xml = XmlReader.Create(content, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxDocumentBytes
        });
        var findings = new List<(string Locator, string Value)>();
        var elements = 0;
        while (xml.Read())
        {
            if (xml.NodeType != XmlNodeType.Element || xml.NamespaceURI != "http://www.w3.org/2000/svg")
            {
                continue;
            }
            var locator = $"{entry.FullName} / {xml.LocalName} #{++elements}";
            foreach (var name in new[] { "role", "aria-label", "aria-labelledby", "aria-describedby", "aria-hidden", "tabindex" })
            {
                if (xml.GetAttribute(name) is { } value)
                {
                    findings.Add(($"{locator} / {name}", value));
                }
            }
            if (xml.LocalName is "title" or "desc")
            {
                findings.Add((locator, "Element declared; rendering or use by an accessibility service is not established."));
            }
            if (findings.Count > MaxItems)
            {
                throw new InvalidDataException("The SVG exceeded the accessibility declaration limit.");
            }
        }
        foreach (var finding in findings)
        {
            Add("declared", "component", "SVG semantics", split, finding.Locator, finding.Value);
        }
    }

    private static byte[] ReadBytes(ZipArchiveEntry entry)
    {
        using var content = entry.Open();
        var bytes = new byte[(int)entry.Length];
        content.ReadExactly(bytes);
        return bytes;
    }

    private void ReadDocument(ZipArchiveEntry entry, string reader, Action parse, CancellationToken token,
        int maxBytes = MaxDocumentBytes)
    {
        var read = false;
        if (_documentsRead < MaxDocuments && _items.Count < MaxItems && entry.Length > 0 && entry.Length <= maxBytes)
        {
            token.ThrowIfCancellationRequested();
            _documentsRead++;
            try
            {
                parse();
                read = true;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or XmlException)
            {
                // Count bounded, unreadable and invalid input as incomplete. Do
                // not copy raw XML, exceptions or unfiltered client content.
            }
        }
        Document(reader, read);
    }

    private void Document(string reader, bool read)
    {
        _files.TryGetValue(reader, out var before);
        _files[reader] = (before.Seen + 1, before.Read + (read ? 1 : 0));
    }

    private void Add(string kind, string scope, string category, string split, string locator, string value)
    {
        if (_items.Count >= MaxItems)
        {
            _itemLimit = true;
            return;
        }
        if (split.Length <= 255 && locator.Length <= 768 && value.Length <= 1024 &&
            !split.Any(char.IsControl) && !locator.Any(char.IsControl))
        {
            _items.Add(new(kind, scope, category, split, locator, value));
        }
        else
        {
            _itemLimit = true;
        }
    }

    private static bool IsResourceXml(string name, string folder)
    {
        if (!name.StartsWith("res/" + folder, StringComparison.Ordinal) || !name.EndsWith(".xml", StringComparison.Ordinal))
        {
            return false;
        }
        var tail = name[(4 + folder.Length)..];
        var slash = tail.IndexOf('/');
        return slash >= 0 && (slash == 0 || tail[0] == '-') && !tail[(slash + 1)..].Contains('/');
    }

    private static bool IsNativeLibrary(string name)
    {
        if (!name.StartsWith("lib/", StringComparison.Ordinal))
        {
            return false;
        }
        var path = name.AsSpan(4);
        var separator = path.IndexOf('/');
        return separator > 0 && !path[(separator + 1)..].Contains('/') &&
            path.EndsWith(".so", StringComparison.Ordinal);
    }
}
