namespace Savepoint.App.Tests.Localization;

// Each lint rule is shown on a bad sample here, so a rule that stops catching anything fails these tests and not just the repository scan.
public class CatalogLintRuleTests
{
    private static IReadOnlyDictionary<string, string> Catalog(params (string Key, string Message)[] entries) =>
        entries.ToDictionary(e => e.Key, e => e.Message);

    [Theory]
    [InlineData("commit")]
    [InlineData("Commits")]
    [InlineData("branch")]
    [InlineData("BRANCHES")]
    [InlineData("checkout")]
    [InlineData("Check out")]
    [InlineData("repo")]
    [InlineData("Repos")]
    [InlineData("repository")]
    [InlineData("Repositories")]
    [InlineData("git")]
    [InlineData("Git")]
    [InlineData("stash")]
    [InlineData("rebase")]
    [InlineData("staging")]
    [InlineData("detached")]
    [InlineData("HEAD")]
    public void A_banned_word_in_a_value_is_reported_with_its_key_and_the_word(string word)
    {
        var catalog = Catalog(("test.sample", $"Please {word} your changes."));

        var problem = Assert.Single(CatalogLint.BannedVocabulary(catalog));

        Assert.Contains("test.sample", problem);
        Assert.Contains(word, problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_banned_word_in_one_value_is_reported()
    {
        var catalog = Catalog(("test.sample", "Commit to the branch."));

        var problems = CatalogLint.BannedVocabulary(catalog);

        Assert.Equal(2, problems.Count);
    }

    [Theory]
    [InlineData("A committee met.")]
    [InlineData("A digital copy.")]
    [InlineData("That is legitimate.")]
    [InlineData("Ask Gitter.")]
    [InlineData("Read the headline.")]
    [InlineData("Head to Settings.")]
    [InlineData("The header row.")]
    public void Words_that_only_contain_a_banned_word_are_allowed(string message)
    {
        var catalog = Catalog(("test.sample", message));

        Assert.Empty(CatalogLint.BannedVocabulary(catalog));
    }

    [Fact]
    public void A_banned_word_in_a_key_is_allowed()
    {
        var catalog = Catalog(("error.gitOperation.checkoutFailed", "A file is in use."));

        Assert.Empty(CatalogLint.BannedVocabulary(catalog));
    }

    [Fact]
    public void Arguments_are_found_in_plain_number_date_plural_and_select_forms_including_nested_ones()
    {
        var arguments = CatalogLint.Arguments(
            "Hi {name}, {n, plural, one {# file by {who}} other {# files}} in {os, select, windows {a Windows {thing}} other {x}} on {when, date, short}: {size, number}");

        Assert.Equal(["name", "n", "who", "os", "thing", "when", "size"], arguments.Select(a => a.Name));
        Assert.Equal("plain", arguments.Single(a => a.Name == "name").Kind);
        Assert.Equal("plural", arguments.Single(a => a.Name == "n").Kind);
        Assert.Equal("date", arguments.Single(a => a.Name == "when").Kind);
        Assert.Equal("number", arguments.Single(a => a.Name == "size").Kind);
        var os = arguments.Single(a => a.Name == "os");
        Assert.Equal("select", os.Kind);
        Assert.Equal(["windows", "other"], os.Options);
    }

    [Fact]
    public void A_quoted_brace_is_not_an_argument()
    {
        var arguments = CatalogLint.Arguments("It's '{x}' here {y}");

        Assert.Equal(["y"], arguments.Select(a => a.Name));
    }

    [Fact]
    public void An_argument_used_twice_is_listed_once_with_its_strongest_kind()
    {
        var arguments = CatalogLint.Arguments("{n} saves: {n, plural, one {# save} other {# saves}}");

        var only = Assert.Single(arguments);
        Assert.Equal("n", only.Name);
        Assert.Equal("plural", only.Kind);
    }

    [Fact]
    public void Messages_that_parse_and_format_in_every_branch_have_no_problems()
    {
        var catalog = Catalog(
            ("test.plain", "Hello"),
            ("test.name", "Hello {name}"),
            ("test.plural", "{n, plural, =0 {none} one {# file} other {# files}}"),
            ("test.os", "{os, select, windows {a Windows folder} macos {a macOS folder} other {a folder}}"),
            ("test.role", "{role, select, install {install} external {saves} other {folder}}"),
            ("test.nested", "{n, plural, one {# file by {who}} other {# files by {who}}}"),
            ("test.date", "On {when, date, short}"),
            ("test.apostrophe", "It's {name}'s turn"));

        Assert.Empty(CatalogLint.FormatProblems("en", catalog));
    }

    [Fact]
    public void A_message_with_an_unclosed_brace_is_reported_with_its_key()
    {
        var catalog = Catalog(("test.bad", "Hello {name"));

        var problem = Assert.Single(CatalogLint.FormatProblems("en", catalog));

        Assert.Contains("test.bad", problem);
    }

    [Fact]
    public void A_select_without_an_other_branch_is_reported_with_its_key()
    {
        var catalog = Catalog(("test.select", "{os, select, windows {a Windows folder} macos {a macOS folder}}"));

        var problem = Assert.Single(CatalogLint.FormatProblems("en", catalog));

        Assert.Contains("test.select", problem);
    }

    [Fact]
    public void A_plural_without_an_other_branch_is_reported_with_its_key()
    {
        var catalog = Catalog(("test.plural", "{n, plural, one {# file}}"));

        var problem = Assert.Single(CatalogLint.FormatProblems("en", catalog));

        Assert.Contains("test.plural", problem);
    }

    [Fact]
    public void A_placeholder_that_the_formatter_swallowed_is_reported_with_its_key()
    {
        var catalog = Catalog(("test.quoted", "Press '{name}' to go"));

        var problem = Assert.Single(CatalogLint.FormatProblems("en", catalog));

        Assert.Contains("test.quoted", problem);
    }

    [Fact]
    public void A_locale_tag_that_is_not_well_formed_is_reported()
    {
        var catalog = Catalog(("test.plain", "Hello"));

        var problem = Assert.Single(CatalogLint.FormatProblems("not a locale", catalog));

        Assert.Contains("not a locale", problem);
    }

    [Fact]
    public void A_locale_file_with_the_same_keys_and_arguments_has_no_problems()
    {
        using var directory = new LangDirectory(
            ("en.json", """{ "a.hello": "Hello {name}", "a.files": "{n, plural, one {# file} other {# files}}" }"""),
            ("de.json", """{ "a.hello": "Hallo {name}", "a.files": "{n, plural, one {# Datei} other {# Dateien}}" }"""));

        Assert.Empty(CatalogLint.AdditionalLocaleProblems(directory.Path));
    }

    [Fact]
    public void A_locale_file_that_is_missing_a_key_is_reported_with_the_locale_and_the_key()
    {
        using var directory = new LangDirectory(
            ("en.json", """{ "a.hello": "Hello", "a.bye": "Bye" }"""),
            ("de.json", """{ "a.hello": "Hallo" }"""));

        var problem = Assert.Single(CatalogLint.AdditionalLocaleProblems(directory.Path));

        Assert.StartsWith("de: ", problem);
        Assert.Contains("a.bye", problem);
    }

    [Fact]
    public void A_locale_file_with_an_extra_key_is_reported_with_the_locale_and_the_key()
    {
        using var directory = new LangDirectory(
            ("en.json", """{ "a.hello": "Hello" }"""),
            ("de.json", """{ "a.hello": "Hallo", "a.extra": "Extra" }"""));

        var problem = Assert.Single(CatalogLint.AdditionalLocaleProblems(directory.Path));

        Assert.StartsWith("de: ", problem);
        Assert.Contains("a.extra", problem);
    }

    [Fact]
    public void A_locale_file_with_different_argument_names_is_reported_with_both_sets()
    {
        using var directory = new LangDirectory(
            ("en.json", """{ "a.hello": "Hello {name}" }"""),
            ("de.json", """{ "a.hello": "Hallo {nome}" }"""));

        var problem = Assert.Single(CatalogLint.AdditionalLocaleProblems(directory.Path));

        Assert.Contains("a.hello", problem);
        Assert.Contains("name", problem);
        Assert.Contains("nome", problem);
    }

    [Fact]
    public void A_locale_file_with_a_message_that_does_not_parse_is_reported()
    {
        using var directory = new LangDirectory(
            ("en.json", """{ "a.hello": "Hello {name}" }"""),
            ("de.json", """{ "a.hello": "Hallo {name" }"""));

        var problem = Assert.Single(CatalogLint.AdditionalLocaleProblems(directory.Path));

        Assert.Contains("a.hello", problem);
        Assert.StartsWith("de: ", problem);
    }

    [Fact]
    public void English_alone_has_no_locale_problems()
    {
        using var directory = new LangDirectory(("en.json", """{ "a.hello": "Hello" }"""));

        Assert.Empty(CatalogLint.AdditionalLocaleProblems(directory.Path));
    }

    [Fact]
    public void A_literal_in_ordinary_code_is_found_with_its_text_and_position()
    {
        var source = "var a = \"plain\";";

        var literal = Assert.Single(SourceLint.Literals(source));

        Assert.Equal("plain", literal.Value);
        Assert.False(literal.Interpolated);
        Assert.Equal(source.IndexOf('"', StringComparison.Ordinal), literal.Start);
    }

    [Fact]
    public void Verbatim_interpolated_and_raw_strings_are_found()
    {
        var source = """"
            var b = @"C:\dir ""q""";
            var c = $"error.{id}";
            var d = """raw "quoted" text""";
            """";

        var literals = SourceLint.Literals(source);

        Assert.Equal([@"C:\dir ""q""", "error.{id}", "raw \"quoted\" text"], literals.Select(l => l.Value));
        Assert.Equal([false, true, false], literals.Select(l => l.Interpolated));
    }

    [Fact]
    public void Escapes_in_a_regular_string_are_decoded()
    {
        var literal = Assert.Single(SourceLint.Literals("""var a = "a\"b\\c";"""));

        Assert.Equal("a\"b\\c", literal.Value);
    }

    [Fact]
    public void Comments_are_not_literals()
    {
        var source = """"
            // "line.comment"
            /* "block.comment" */
            /// <summary>"doc.comment"</summary>
            var real = "real.literal";
            """";

        Assert.Equal(["real.literal"], SourceLint.Literals(source).Select(l => l.Value));
    }

    [Fact]
    public void A_char_literal_does_not_start_a_string()
    {
        var source = """"
            var quote = '"';
            var apostrophe = '\'';
            var after = "after";
            """";

        Assert.Equal(["after"], SourceLint.Literals(source).Select(l => l.Value));
    }

    [Fact]
    public void A_literal_inside_an_interpolation_hole_is_found_too()
    {
        var source = """var a = $"{catalog.Format("a.b")}.tail";""";

        var values = SourceLint.Literals(source).Select(l => l.Value);

        Assert.Contains("a.b", values);
    }

    [Fact]
    public void Keys_passed_to_the_catalog_api_are_found_and_other_formats_are_not()
    {
        var source = """"
            var a = catalog.Format("a.one");
            var b = L.Loc("b.two", args);
            var c = new LocExtension("c.three");
            var d = string.Format("{0} is not a key", 1);
            var e = Format(key);
            var f = catalog.Format("d.four", new Dictionary<string, object?>());
            var g = String.Format("e.five {0}", 1);
            var h = catalog.Format(
                "f.six");
            var i = catalog.Format(key: "g.seven");
            // catalog.Format("h.eight");
            """";

        var keys = SourceLint.CatalogCallKeys(source);

        Assert.Equal(["a.one", "b.two", "c.three", "d.four", "f.six", "g.seven"], keys);
    }

    [Fact]
    public void Xaml_loc_keys_are_found_in_every_form()
    {
        var axaml = """"
            <TextBlock Text="{loc:Loc app.name}"
                       Tag="{loc:Loc Key=app.placeholder.heading}"
                       ToolTip.Tip="{loc:Loc a.b}"
                       AutomationProperties.Name="{Loc c.d}"
                       Watermark="{ loc:Loc  e.f }"
                       Title="{loc:Loc 'g.h'}"
                       Header="{Binding Loc}" />
            """";

        var keys = SourceLint.XamlLocKeys(axaml);

        Assert.Equal(["app.name", "app.placeholder.heading", "a.b", "c.d", "e.f", "g.h"], keys);
    }

    [Fact]
    public void Xaml_loc_keys_inside_comments_are_ignored()
    {
        var axaml = """
            <!-- Pair these with {theme:Upper {loc:Loc ...}} or {loc:Loc x.y}. -->
            <TextBlock Text="{loc:Loc app.name}" />
            """;

        Assert.Equal(["app.name"], SourceLint.XamlLocKeys(axaml));
    }

    [Fact]
    public void A_key_the_code_asks_for_that_the_catalog_lacks_is_reported_with_the_file()
    {
        var references = new Dictionary<string, IReadOnlyList<string>>
        {
            ["app.name"] = ["src/A.cs"],
            ["error.missing.thing"] = ["src/B.cs", "src/C.axaml"],
        };

        var problem = Assert.Single(SourceLint.MissingKeys(references, ["app.name"]));

        Assert.Contains("error.missing.thing", problem);
        Assert.Contains("src/B.cs", problem);
    }

    [Fact]
    public void A_catalog_key_that_no_code_uses_is_reported()
    {
        var problem = Assert.Single(SourceLint.UnusedKeys(["app.name", "app.dead"], ["app.name"]));

        Assert.Contains("app.dead", problem);
    }

    [Theory]
    [InlineData("""var id = $"error.{code}";""")]
    [InlineData("""var id = "error." + code;""")]
    [InlineData("""var id = "validation.game." + field;""")]
    [InlineData("""var id = string.Concat("error.", code);""")]
    [InlineData("""var id = string.Format("error.{0}", code);""")]
    [InlineData("""var id = "error.capture" + suffix;""")]
    [InlineData("""var id = prefix + "error.cancelled";""")]
    public void A_catalog_key_built_at_run_time_is_reported(string source)
    {
        var problem = Assert.Single(SourceLint.DynamicKeyConstruction("src/Sample.cs", source, ["error", "validation", "app"]));

        Assert.Contains("src/Sample.cs", problem);
    }

    [Theory]
    [InlineData("""const string A = "error.launch.canceled";""")]
    [InlineData("""// var id = "error." + code;""")]
    [InlineData("""var s = "errors happen";""")]
    [InlineData("""var s = $"Saved {path}";""")]
    [InlineData("""var s = flag ? "error.a.b" : "error.c.d";""")]
    [InlineData("""var s = new Dictionary<string, int> { ["error.a.b"] = 1 };""")]
    [InlineData("""var s = "app.exe";""")]
    public void A_complete_literal_key_is_not_reported(string source)
    {
        Assert.Empty(SourceLint.DynamicKeyConstruction("src/Sample.cs", source, ["error", "validation", "app"]));
    }

    [Theory]
    [InlineData("""<TextBlock Text="Hello" />""", "Text")]
    [InlineData("""<Button Content="Save" />""", "Content")]
    [InlineData("""<Window Title="Settings" />""", "Title")]
    [InlineData("""<Expander Header="Name" />""", "Header")]
    [InlineData("""<TextBox Watermark="Search" />""", "Watermark")]
    [InlineData("""<TextBox PlaceholderText="Type here" />""", "PlaceholderText")]
    [InlineData("""<Button ToolTip.Tip="Close" />""", "ToolTip.Tip")]
    [InlineData("""<Button AutomationProperties.Name="Main" />""", "AutomationProperties.Name")]
    [InlineData("""<Button AutomationProperties.HelpText="Press to save" />""", "AutomationProperties.HelpText")]
    [InlineData("""<TextBlock Text="{}Hello" />""", "Text")]
    [InlineData("""<TextBlock Text="{StaticResource Greeting}" />""", "Text")]
    [InlineData("""<Button><Button.Content>Save</Button.Content></Button>""", "Button.Content")]
    [InlineData("""<TextBlock>Hello there</TextBlock>""", "TextBlock")]
    [InlineData("""<Button>Save</Button>""", "Button")]
    [InlineData("""<Style><Setter Property="Text" Value="Hello" /></Style>""", "Setter")]
    public void Hardcoded_text_in_xaml_is_reported_with_the_file_and_the_property(string body, string property)
    {
        var problem = Assert.Single(SourceLint.HardcodedXamlText("src/Sample.axaml", Xaml(body)));

        Assert.Contains("src/Sample.axaml", problem);
        Assert.Contains(property, problem);
    }

    [Theory]
    [InlineData("""<TextBlock Text="{loc:Loc app.name}" />""")]
    [InlineData("""<TextBlock Text="{loc:Loc Key=app.name}" />""")]
    [InlineData("""<TextBlock Text="{Binding Name}" />""")]
    [InlineData("""<TextBlock Text="{CompiledBinding Name}" />""")]
    [InlineData("""<TextBlock Text="{ReflectionBinding Name}" />""")]
    [InlineData("""<Button Content="{x:Static local:Labels.Save}" />""")]
    [InlineData("""<TextBlock Text="&#xE145;" />""")]
    [InlineData("""<TextBlock Text="…" />""")]
    [InlineData("""<TextBlock Text="/" />""")]
    [InlineData("""<TextBlock Text="12" />""")]
    [InlineData("""<Button ToolTip.Tip="{loc:Loc a.b}" />""")]
    [InlineData("""<TextBox Watermark="{loc:Loc a.b}" />""")]
    [InlineData("""<TextBlock x:Name="HeadingText" FontSize="28" Tag="Hello" />""")]
    [InlineData("""<Button><Button.Content><TextBlock Text="{loc:Loc a.b}" /></Button.Content></Button>""")]
    [InlineData("""<TextBlock>   </TextBlock>""")]
    [InlineData("""<Style><Setter Property="Text" Value="{loc:Loc a.b}" /></Style>""")]
    [InlineData("""<Style><Setter Property="FontSize" Value="Large" /></Style>""")]
    [InlineData("""<ContentPresenter Content="{TemplateBinding Content}" />""")]
    [InlineData("""<TextBox Text="{TemplateBinding Text, Mode=TwoWay}" />""")]
    public void Bound_localized_or_letterless_xaml_text_is_not_reported(string body)
    {
        Assert.Empty(SourceLint.HardcodedXamlText("src/Sample.axaml", Xaml(body)));
    }

    private static string Xaml(string body) =>
        $"""
        <UserControl xmlns="https://github.com/avaloniaui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:loc="using:Savepoint.App.Localization"
                     xmlns:local="using:Savepoint.App">
          {body}
        </UserControl>
        """;

    private sealed class LangDirectory : IDisposable
    {
        public LangDirectory(params (string Name, string Json)[] files)
        {
            Path = Directory.CreateTempSubdirectory("savepoint-lang-").FullName;
            foreach (var (name, json) in files)
            {
                File.WriteAllText(System.IO.Path.Combine(Path, name), json);
            }
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
