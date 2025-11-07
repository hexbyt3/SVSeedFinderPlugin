using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PKHeX.Core;
using SVSeedFinderPlugin.Helpers;
using static PKHeX.Core.ShinyUtil;

namespace SVSeedFinderPlugin.GUI;

/// <summary>
/// Gen 9 Seed Finder Plugin for PKHeX
/// </summary>
/// <author>hexbyt3</author>
/// <description>Tool for searching Generation 9 Tera Raid seeds that match specific criteria</description>

/// <summary>
/// Form for searching Generation 9 Tera Raid seeds that match specific criteria
/// </summary>
public sealed partial class Gen9SeedFinderForm : Form
{
    private readonly ISaveFileProvider _saveFileEditor;
    private readonly IPKMView _pkmEditor;
    private CancellationTokenSource? _searchCts;
    private List<SeedResult> _results = [];
    private Dictionary<int, SeedResult> _gridRowToResult = new(); // Maps grid row index to actual result
    private List<ITeraRaid9> _cachedSpeciesEncounters = [];
    private EncounterSource _availableSources;
    private List<ComboItem> _allSpecies = [];

    // Preview panel components
    private Panel _previewPanel = null!;
    private PictureBox _previewSprite = null!;
    private Label _previewTitle = null!;
    private Label _previewDetails = null!;
    private Label _previewStats = null!;
    private Label _previewMoves = null!;
    private static readonly HttpClient _httpClient = new();

    [Flags]
    private enum EncounterSource
    {
        None = 0,
        Base = 1 << 0,
        DLC1 = 1 << 1,
        DLC2 = 1 << 2,
        Dist = 1 << 3,
        Might = 1 << 4,
    }

    public Gen9SeedFinderForm(ISaveFileProvider saveFileEditor, IPKMView pkmEditor)
    {
        _saveFileEditor = saveFileEditor;
        _pkmEditor = pkmEditor;
        InitializeComponent();
        InitializePreviewPanel();
        LoadSpeciesList();
        LoadTrainerData();

        // Cancel search when form is closing
        FormClosing += OnFormClosing;
    }

    /// <summary>
    /// Handles form closing to cancel any running search
    /// </summary>
    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        // Cancel any running search operation
        _searchCts?.Cancel();
    }

    private void InitializePreviewPanel()
    {
        _previewPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 200,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SystemColors.Control,
            Visible = false
        };

        _previewSprite = new PictureBox
        {
            Location = new Point(10, 10),
            Size = new Size(68, 56),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            BorderStyle = BorderStyle.FixedSingle
        };

        _previewTitle = new Label
        {
            Location = new Point(85, 10),
            Size = new Size(250, 20),
            Font = new Font(Font.FontFamily, 10, FontStyle.Bold)
        };

        _previewDetails = new Label
        {
            Location = new Point(85, 35),
            Size = new Size(250, 60),
            AutoSize = false
        };

        _previewStats = new Label
        {
            Location = new Point(340, 10),
            Size = new Size(180, 180),
            AutoSize = false,
            Font = new Font("Consolas", 9)
        };

        _previewMoves = new Label
        {
            Location = new Point(85, 100),
            Size = new Size(250, 90),
            AutoSize = false
        };

        _previewPanel.Controls.AddRange([
            _previewSprite, _previewTitle, _previewDetails, _previewStats, _previewMoves
        ]);

        // Add preview panel to results panel
        resultsPanel?.Controls.Add(_previewPanel);

        // Adjust grid size when preview is shown
        if (resultsGrid != null)
        {
            resultsGrid.SelectionChanged += ResultsGrid_SelectionChanged;
        }
    }

    private void ResultsGrid_SelectionChanged(object? sender, EventArgs e)
    {
        if (resultsGrid.SelectedRows.Count == 0 || _previewPanel == null)
        {
            if (_previewPanel != null)
                _previewPanel.Visible = false;
            return;
        }

        var rowIndex = resultsGrid.SelectedRows[0].Index;
        if (_gridRowToResult.TryGetValue(rowIndex, out var result))
        {
            UpdatePreviewPanel(result);

            // Adjust grid height to accommodate preview
            if (!_previewPanel.Visible)
            {
                _previewPanel.Visible = true;
                resultsGrid.Height = resultsPanel.Height - _previewPanel.Height;
            }
        }
    }

    private void UpdatePreviewPanel(SeedResult result)
    {
        if (_previewSprite == null || _previewTitle == null || _previewDetails == null ||
            _previewStats == null || _previewMoves == null)
        {
            return;
        }

        var pk = result.Pokemon;
        var enc = result.Encounter;

        // Load sprite asynchronously
        _ = LoadPokemonSpriteAsync(pk);

        // Set title
        var speciesName = GameInfo.Strings.specieslist[pk.Species];
        var formName = pk.Form > 0 ? $" ({FormConverter.GetFormList(pk.Species, GameInfo.Strings.types, GameInfo.Strings.forms, GameInfo.GenderSymbolASCII, EntityContext.Gen9)[pk.Form]})" : "";
        _previewTitle.Text = $"{speciesName}{formName} {(pk.IsShiny ? "★" : "")}";
        _previewTitle.ForeColor = pk.IsShiny ? Color.Gold : SystemColors.ControlText;

        // Set details
        var details = new List<string>
        {
            $"Seed: {result.Seed:X8}",
            $"Nature: {pk.Nature} | Gender: {GetGenderSymbol(pk.Gender)}",
            $"Ability: {GetAbilityName(pk)} ({GetAbilityType(pk)})",
            $"Tera Type: {pk.TeraTypeOriginal}"
        };

        if (enc is EncounterMight9)
            details.Add("7★ Mighty Raid (Mightiest Mark)");
        else if (enc is EncounterDist9)
            details.Add($"{enc.Stars}★ Event Distribution");
        else
            details.Add($"{enc.Stars}★ Standard Raid");

        _previewDetails.Text = string.Join("\n", details);

        // Set stats
        var stats = new[]
        {
            "Stats:",
            $"HP:  {pk.IV_HP,2} IV | {pk.Stat_HPMax,3} Total",
            $"Atk: {pk.IV_ATK,2} IV | {pk.Stat_ATK,3} Total",
            $"Def: {pk.IV_DEF,2} IV | {pk.Stat_DEF,3} Total",
            $"SpA: {pk.IV_SPA,2} IV | {pk.Stat_SPA,3} Total",
            $"SpD: {pk.IV_SPD,2} IV | {pk.Stat_SPD,3} Total",
            $"Spe: {pk.IV_SPE,2} IV | {pk.Stat_SPE,3} Total",
            "",
            $"Height: {pk.HeightScalar} | Weight: {pk.WeightScalar}",
            $"Scale: {pk.Scale} ({GetScaleCategory(pk.Scale)})"
        };
        _previewStats.Text = string.Join("\n", stats);

        // Set moves
        var moveNames = new List<string>();
        for (int i = 0; i < 4; i++)
        {
            var move = pk.GetMove(i);
            if (move != 0)
            {
                var moveName = GameInfo.Strings.movelist[move];
                moveNames.Add($"• {moveName}");
            }
        }
        _previewMoves.Text = moveNames.Count > 0 ? "Moves:\n" + string.Join("\n", moveNames) : "";
    }

    private static string GetGenderSymbol(int gender) => gender switch
    {
        0 => "♂",
        1 => "♀",
        _ => "-"
    };

    private static string GetScaleCategory(byte scale) => scale switch
    {
        0 => "XXXS",
        >= 1 and <= 24 => "XXS",
        >= 25 and <= 59 => "XS",
        >= 60 and <= 99 => "S",
        >= 100 and <= 155 => "AV",
        >= 156 and <= 195 => "L",
        >= 196 and <= 230 => "XL",
        >= 231 and <= 254 => "XXL",
        255 => "XXXL"
    };

    private static string GetAbilityType(PK9 pk) => pk.AbilityNumber switch
    {
        1 => "Ability 1",
        2 => "Ability 2",
        4 => "Hidden",
        _ => "?"
    };

    private async Task LoadPokemonSpriteAsync(PK9 pk)
    {
        try
        {
            var url = GetPokemonImageUrl(pk);
            using var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                using var stream = await response.Content.ReadAsStreamAsync();
                var image = Image.FromStream(stream);

                // Update UI on main thread
                if (_previewSprite.InvokeRequired)
                {
                    _previewSprite.Invoke(() => _previewSprite.Image = image);
                }
                else
                {
                    _previewSprite.Image = image;
                }
            }
        }
        catch
        {
            // If image fails to load, just show empty box
            if (_previewSprite.InvokeRequired)
            {
                _previewSprite.Invoke(() => _previewSprite.Image = null);
            }
            else
            {
                _previewSprite.Image = null;
            }
        }
    }

    private static string GetPokemonImageUrl(PK9 pk)
    {
        var baseLink = "https://raw.githubusercontent.com/hexbyt3/HomeImages/master/128x128/poke_capture_0001_000_mf_n_00000000_f_n.png".Split('_');

        // Species number formatting
        baseLink[2] = pk.Species < 10 ? $"000{pk.Species}" :
                      pk.Species < 100 ? $"00{pk.Species}" :
                      pk.Species < 1000 ? $"0{pk.Species}" :
                      $"{pk.Species}";

        // Form number formatting
        baseLink[3] = pk.Form < 10 ? $"00{pk.Form}" : $"0{pk.Form}";

        // Gender designation
        baseLink[4] = pk.PersonalInfo.OnlyFemale ? "fo" :
                      pk.PersonalInfo.OnlyMale ? "mo" :
                      pk.PersonalInfo.Genderless ? "uk" :
                      GetGenderDesignation(pk);

        // Gigantamax (always "n" for raid finder)
        baseLink[5] = "n";

        // Form argument (always 0 for raid finder)
        baseLink[6] = "00000000";

        // Shiny status
        baseLink[8] = pk.IsShiny ? "r.png" : "n.png";

        return string.Join("_", baseLink);
    }

    private static string GetGenderDesignation(PK9 pk)
    {
        // Check for gender-dependent species
        var genderDependentSpecies = new[]
        {
            (int)Species.Venusaur, (int)Species.Butterfree, (int)Species.Rattata, (int)Species.Raticate,
            (int)Species.Pikachu, (int)Species.Zubat, (int)Species.Golbat, (int)Species.Gloom,
            (int)Species.Vileplume, (int)Species.Kadabra, (int)Species.Alakazam, (int)Species.Doduo,
            (int)Species.Dodrio, (int)Species.Hypno, (int)Species.Goldeen, (int)Species.Seaking,
            (int)Species.Scyther, (int)Species.Magikarp, (int)Species.Gyarados, (int)Species.Eevee,
            (int)Species.Meganium, (int)Species.Ledyba, (int)Species.Ledian, (int)Species.Xatu,
            (int)Species.Sudowoodo, (int)Species.Politoed, (int)Species.Aipom, (int)Species.Wooper,
            (int)Species.Quagsire, (int)Species.Murkrow, (int)Species.Wobbuffet, (int)Species.Girafarig,
            (int)Species.Gligar, (int)Species.Steelix, (int)Species.Scizor, (int)Species.Heracross,
            (int)Species.Sneasel, (int)Species.Ursaring, (int)Species.Piloswine, (int)Species.Octillery,
            (int)Species.Houndoom, (int)Species.Donphan, (int)Species.Torchic, (int)Species.Combusken,
            (int)Species.Blaziken, (int)Species.Beautifly, (int)Species.Dustox, (int)Species.Ludicolo,
            (int)Species.Nuzleaf, (int)Species.Shiftry, (int)Species.Swalot, (int)Species.Camerupt,
            (int)Species.Cacturne, (int)Species.Milotic, (int)Species.Relicanth, (int)Species.Starly,
            (int)Species.Staravia, (int)Species.Staraptor, (int)Species.Bidoof, (int)Species.Bibarel,
            (int)Species.Kricketot, (int)Species.Kricketune, (int)Species.Shinx, (int)Species.Luxio,
            (int)Species.Luxray, (int)Species.Roserade, (int)Species.Combee, (int)Species.Pachirisu,
            (int)Species.Buizel, (int)Species.Floatzel, (int)Species.Ambipom, (int)Species.Gible,
            (int)Species.Gabite, (int)Species.Garchomp, (int)Species.Hippopotas, (int)Species.Hippowdon,
            (int)Species.Croagunk, (int)Species.Toxicroak, (int)Species.Finneon, (int)Species.Lumineon,
            (int)Species.Snover, (int)Species.Abomasnow, (int)Species.Weavile, (int)Species.Rhyperior,
            (int)Species.Tangrowth, (int)Species.Mamoswine, (int)Species.Unfezant, (int)Species.Frillish,
            (int)Species.Jellicent, (int)Species.Pyroar, (int)Species.Meowstic, (int)Species.Indeedee,
            (int)Species.Basculegion, (int)Species.Oinkologne
        };

        if (genderDependentSpecies.Contains(pk.Species) && pk.Form == 0)
        {
            return pk.Gender == 0 ? "md" : "fd";
        }

        return "mf";
    }

    private void LoadTrainerData()
    {
        var sav = _saveFileEditor.SAV;
        // Gen 9 uses the Gen 7+ display format (6-digit TID, 4-digit SID)
        // Convert from internal 16-bit values to display format
        uint id32 = ((uint)sav.SID16 << 16) | sav.TID16;
        tidNum.Value = id32 % 1000000; // TID7 (0-999999)
        sidNum.Value = id32 / 1000000; // SID7 (0-4294)
    }

    /// <summary>
    /// Loads the species list into the combo box
    /// </summary>
    private void LoadSpeciesList()
    {
        var species = new List<ComboItem>();
        var names = GameInfo.Strings.specieslist;

        for (int i = 1; i < names.Length; i++)
        {
            if (PersonalTable.SV.IsPresentInGame((ushort)i, 0))
                species.Add(new ComboItem(names[i], i));
        }

        _allSpecies = species;
        speciesCombo.DisplayMember = "Text";
        speciesCombo.ValueMember = "Value";
        speciesCombo.DataSource = species;
    }

    /// <summary>
    /// Handles the search box text change event to filter species
    /// </summary>
    private void SpeciesSearchBox_TextChanged(object? sender, EventArgs e)
    {
        var searchText = speciesSearchBox.Text.Trim();

        if (string.IsNullOrEmpty(searchText))
        {
            // Show all species if search is empty
            speciesCombo.DataSource = _allSpecies;
            return;
        }

        // Filter species based on search text (case-insensitive)
        var filteredSpecies = _allSpecies.Where(s =>
            s.Text.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();

        // Update the combo box with filtered results
        if (filteredSpecies.Count > 0)
        {
            speciesCombo.DataSource = filteredSpecies;

            // If there's only one result, auto-select it
            if (filteredSpecies.Count == 1)
            {
                speciesCombo.SelectedIndex = 0;
            }
        }
        else
        {
            // Show empty list if no matches
            speciesCombo.DataSource = new List<ComboItem>();
        }
    }

    /// <summary>
    /// Handles species selection change
    /// </summary>
    private void SpeciesCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (speciesCombo.SelectedValue is not int species)
            return;

        UpdateEncounterList(species);
        UpdateSourceDisplay();
        ValidateCurrentSelection();
    }

    /// <summary>
    /// Updates the status to show which sources contain the selected species
    /// </summary>
    private void UpdateSourceDisplay()
    {
        var sources = new List<string>();
        if (_availableSources.HasFlag(EncounterSource.Base))
            sources.Add("Paldea");
        if (_availableSources.HasFlag(EncounterSource.DLC1))
            sources.Add("Kitakami");
        if (_availableSources.HasFlag(EncounterSource.DLC2))
            sources.Add("Blueberry");
        if (_availableSources.HasFlag(EncounterSource.Dist))
            sources.Add("Event");
        if (_availableSources.HasFlag(EncounterSource.Might))
            sources.Add("7★");

        statusLabel.Text = sources.Count > 0
            ? $"Available in: {string.Join(", ", sources)}"
            : "No encounters found";
    }


    /// <summary>
    /// Updates the encounter list for the selected species
    /// </summary>
    private void UpdateEncounterList(int species)
    {
        var allEncounters = new List<ITeraRaid9>();
        _availableSources = EncounterSource.None;

        // Get ALL encounters for this species (all forms)
        var baseEnc = GetAllEncountersForSpecies(Encounters9.TeraBase, species);
        if (baseEnc.Count > 0)
        {
            allEncounters.AddRange(baseEnc);
            _availableSources |= EncounterSource.Base;
        }

        var dlc1Enc = GetAllEncountersForSpecies(Encounters9.TeraDLC1, species);
        if (dlc1Enc.Count > 0)
        {
            allEncounters.AddRange(dlc1Enc);
            _availableSources |= EncounterSource.DLC1;
        }

        var dlc2Enc = GetAllEncountersForSpecies(Encounters9.TeraDLC2, species);
        if (dlc2Enc.Count > 0)
        {
            allEncounters.AddRange(dlc2Enc);
            _availableSources |= EncounterSource.DLC2;
        }

        var distEnc = GetAllEncountersForSpecies(Encounters9.Dist, species);
        if (distEnc.Count > 0)
        {
            allEncounters.AddRange(distEnc);
            _availableSources |= EncounterSource.Dist;
        }

        var mightEnc = GetAllEncountersForSpecies(Encounters9.Might, species);
        if (mightEnc.Count > 0)
        {
            allEncounters.AddRange(mightEnc);
            _availableSources |= EncounterSource.Might;
        }

        // Cache in priority order: Dist and Might have priority
        _cachedSpeciesEncounters = [.. distEnc, .. mightEnc, .. baseEnc, .. dlc1Enc, .. dlc2Enc];

        // Remove duplicates based on encounter properties
        var uniqueEncounters = new List<ITeraRaid9>();
        var seen = new HashSet<string>();

        foreach (var enc in allEncounters)
        {
            var key = GetEncounterKey(enc);
            if (seen.Add(key))
            {
                uniqueEncounters.Add(enc);
            }
        }

        var items = uniqueEncounters.Select(e => new EncounterItem(e)).ToList();
        items.Insert(0, new EncounterItem(null)); // Any encounter

        encounterCombo.DisplayMember = "Text";
        encounterCombo.ValueMember = "Value";
        encounterCombo.DataSource = items;

        // Add event handler for encounter selection
        encounterCombo.SelectedIndexChanged -= EncounterCombo_SelectedIndexChanged;
        encounterCombo.SelectedIndexChanged += EncounterCombo_SelectedIndexChanged;
    }

    /// <summary>
    /// Gets a unique key for an encounter to detect duplicates
    /// </summary>
    private static string GetEncounterKey(ITeraRaid9 encounter)
    {
        return $"{encounter.Species}_{encounter.Form}_{encounter.Stars}_{encounter.Index}_{encounter.GetType().Name}";
    }

    /// <summary>
    /// Handles encounter selection change for validation
    /// </summary>
    private void EncounterCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        ValidateCurrentSelection();
    }

    /// <summary>
    /// Validates the current selection and updates UI to show constraints
    /// </summary>
    private void ValidateCurrentSelection()
    {
        var selectedEncounter = (encounterCombo.SelectedItem as EncounterItem)?.Encounter;
        if (selectedEncounter == null)
        {
            // No specific encounter selected, reset constraints
            ResetConstraintHighlights();
            return;
        }

        var warnings = new List<string>();

        // Check shiny constraints
        if (selectedEncounter.Shiny != Shiny.Random)
        {
            var requiredShiny = selectedEncounter.Shiny switch
            {
                Shiny.Never => 1, // "Never" index
                Shiny.Always => 2, // "Always" index
                _ => 0
            };

            if (shinyCombo.SelectedIndex != 0 && shinyCombo.SelectedIndex != requiredShiny)
            {
                warnings.Add($"This encounter is {(selectedEncounter.Shiny == Shiny.Never ? "never shiny" : "always shiny")}");
                shinyCombo.BackColor = Color.MistyRose;
            }
            else
            {
                shinyCombo.BackColor = SystemColors.Window;
            }
        }
        else
        {
            shinyCombo.BackColor = SystemColors.Window;
        }

        // Check IV constraints
        bool hasIVConstraints = false;
        if (selectedEncounter is EncounterMight9 might)
        {
            // 7* raids always have 6 flawless IVs
            hasIVConstraints = true;
            ValidateIVConstraints(6, might.IVs, warnings);
        }
        else if (selectedEncounter is EncounterDist9 dist)
        {
            hasIVConstraints = true;
            ValidateIVConstraints(dist.FlawlessIVCount, dist.IVs, warnings);
        }
        else if (selectedEncounter is EncounterTera9 tera)
        {
            hasIVConstraints = tera.FlawlessIVCount > 0;
            ValidateIVConstraints(tera.FlawlessIVCount, default, warnings);
        }

        if (!hasIVConstraints)
        {
            ResetIVHighlights();
        }

        // Check gender constraints
        if (selectedEncounter.Gender != FixedGenderUtil.GenderRandom)
        {
            var requiredGender = selectedEncounter.Gender switch
            {
                0 => 1, // Male
                1 => 2, // Female
                2 => 3, // Genderless
                _ => 0
            };

            if (genderCombo.SelectedIndex != 0 && genderCombo.SelectedIndex != requiredGender)
            {
                warnings.Add($"This encounter has fixed gender: {GetGenderName(selectedEncounter.Gender)}");
                genderCombo.BackColor = Color.MistyRose;
            }
            else
            {
                genderCombo.BackColor = SystemColors.Window;
            }
        }
        else
        {
            genderCombo.BackColor = SystemColors.Window;
        }

        // Check nature constraints
        if (selectedEncounter is IFixedNature fn && fn.Nature != Nature.Random)
        {
            var requiredNature = (int)fn.Nature + 1; // +1 because index 0 is "Any"

            if (natureCombo.SelectedIndex != 0 && natureCombo.SelectedIndex != requiredNature)
            {
                warnings.Add($"This encounter has fixed nature: {fn.Nature}");
                natureCombo.BackColor = Color.MistyRose;
            }
            else
            {
                natureCombo.BackColor = SystemColors.Window;
            }
        }
        else
        {
            natureCombo.BackColor = SystemColors.Window;
        }

        // Check ability constraints
        if (selectedEncounter.Ability != AbilityPermission.Any12H)
        {
            var validAbility = IsAbilitySelectionValid(selectedEncounter.Ability, (AbilityPermission)GetAbilityPermission());
            if (!validAbility && abilityCombo.SelectedIndex != 0)
            {
                warnings.Add($"This encounter has ability constraint: {GetAbilityConstraintText(selectedEncounter.Ability)}");
                abilityCombo.BackColor = Color.MistyRose;
            }
            else
            {
                abilityCombo.BackColor = SystemColors.Window;
            }
        }
        else
        {
            abilityCombo.BackColor = SystemColors.Window;
        }

        // Check scale constraints
        ValidateScaleConstraints(selectedEncounter, warnings);

        // Update status with warnings
        if (warnings.Count > 0)
        {
            statusLabel.Text = $"⚠️ {string.Join(" | ", warnings)}";
            statusLabel.ForeColor = Color.DarkRed;
        }
        else
        {
            UpdateSourceDisplay();
            statusLabel.ForeColor = SystemColors.ControlText;
        }
    }

    /// <summary>
    /// Validates scale constraints for the selected encounter
    /// </summary>
    private void ValidateScaleConstraints(ITeraRaid9 selectedEncounter, List<string> warnings)
    {
        // Extract scale type and value based on encounter type
        SizeType9? scaleType = null;
        byte? fixedScale = null;
        
        if (selectedEncounter is EncounterMight9 might)
        {
            scaleType = might.ScaleType;
            if (might.ScaleType == SizeType9.VALUE)
                fixedScale = might.Scale;
        }
        else if (selectedEncounter is EncounterDist9 dist)
        {
            scaleType = dist.ScaleType;
            if (dist.ScaleType == SizeType9.VALUE)
                fixedScale = dist.Scale;
        }
        
        // If there's no scale constraint, reset highlights and return
        if (scaleType == null || scaleType == SizeType9.RANDOM)
        {
            scaleMin.BackColor = SystemColors.Window;
            scaleMax.BackColor = SystemColors.Window;
            return;
        }
        
        var minValue = (byte)scaleMin.Value;
        var maxValue = (byte)scaleMax.Value;
        bool hasInvalidScale = false;
        
        if (scaleType == SizeType9.VALUE && fixedScale.HasValue)
        {
            // Fixed scale value - check if the range includes it
            if (minValue > fixedScale.Value || maxValue < fixedScale.Value)
            {
                warnings.Add($"This encounter has fixed scale: {fixedScale.Value}");
                hasInvalidScale = true;
            }
        }
        else if (scaleType != SizeType9.RANDOM)
        {
            // Size category constraint - check if range overlaps with the category
            var (categoryMin, categoryMax) = GetScaleRange(scaleType.Value);
            
            // Check if there's any overlap between user range and category range
            if (maxValue < categoryMin || minValue > categoryMax)
            {
                warnings.Add($"This encounter has scale type: {GetScaleTypeName(scaleType.Value)} ({categoryMin}-{categoryMax})");
                hasInvalidScale = true;
            }
        }
        
        // Update visual feedback
        if (hasInvalidScale)
        {
            scaleMin.BackColor = Color.MistyRose;
            scaleMax.BackColor = Color.MistyRose;
        }
        else
        {
            scaleMin.BackColor = SystemColors.Window;
            scaleMax.BackColor = SystemColors.Window;
        }
    }

    /// <summary>
    /// Gets the scale range for a given SizeType9
    /// </summary>
    private static (byte min, byte max) GetScaleRange(SizeType9 type) => type switch
    {
        SizeType9.XS => (0, 15),
        SizeType9.S => (16, 47),
        SizeType9.M => (48, 207),
        SizeType9.L => (208, 239),
        SizeType9.XL => (240, 255),
        _ => (0, 255)
    };

    /// <summary>
    /// Gets the display name for a SizeType9
    /// </summary>
    private static string GetScaleTypeName(SizeType9 type) => type switch
    {
        SizeType9.XS => "Extra Small",
        SizeType9.S => "Small",
        SizeType9.M => "Average",
        SizeType9.L => "Large",
        SizeType9.XL => "Extra Large",
        SizeType9.VALUE => "Fixed",
        _ => "Random"
    };

    /// <summary>
    /// Validates IV constraints and highlights invalid selections
    /// </summary>
    private void ValidateIVConstraints(byte flawlessCount, IndividualValueSet fixedIVs, List<string> warnings)
    {
        bool hasInvalidIV = false;
        var ivControls = new[]
        {
            (Min: ivHpMin, Max: ivHpMax, Name: "HP", Fixed: fixedIVs.HP),
            (Min: ivAtkMin, Max: ivAtkMax, Name: "Atk", Fixed: fixedIVs.ATK),
            (Min: ivDefMin, Max: ivDefMax, Name: "Def", Fixed: fixedIVs.DEF),
            (Min: ivSpaMin, Max: ivSpaMax, Name: "SpA", Fixed: fixedIVs.SPA),
            (Min: ivSpdMin, Max: ivSpdMax, Name: "SpD", Fixed: fixedIVs.SPD),
            (Min: ivSpeMin, Max: ivSpeMax, Name: "Spe", Fixed: fixedIVs.SPE),
        };

        // Check fixed IVs first
        if (fixedIVs.IsSpecified)
        {
            foreach (var (Min, Max, Name, Fixed) in ivControls)
            {
                if (Fixed != -1)
                {
                    if (Min.Value > Fixed || Max.Value < Fixed)
                    {
                        Min.BackColor = Color.MistyRose;
                        Max.BackColor = Color.MistyRose;
                        hasInvalidIV = true;
                    }
                    else
                    {
                        Min.BackColor = SystemColors.Window;
                        Max.BackColor = SystemColors.Window;
                    }
                }
                else
                {
                    Min.BackColor = SystemColors.Window;
                    Max.BackColor = SystemColors.Window;
                }
            }

            if (hasInvalidIV)
            {
                warnings.Add($"This encounter has fixed IVs: {GetFixedIVString(fixedIVs)}");
            }
        }
        else if (flawlessCount > 0)
        {
            // For flawless IVs without fixed positions, check if user is excluding 31
            int possibleFlawlessSlots = 0;
            foreach (var (Min, Max, Name, _) in ivControls)
            {
                if (Max.Value >= 31)
                    possibleFlawlessSlots++;
            }

            if (possibleFlawlessSlots < flawlessCount)
            {
                // Highlight all IV controls that don't allow 31
                foreach (var (Min, Max, Name, _) in ivControls)
                {
                    if (Max.Value < 31)
                    {
                        Max.BackColor = Color.MistyRose;
                        hasInvalidIV = true;
                    }
                    else
                    {
                        Min.BackColor = SystemColors.Window;
                        Max.BackColor = SystemColors.Window;
                    }
                }

                warnings.Add($"This encounter has {flawlessCount} guaranteed perfect IVs");
            }
            else
            {
                ResetIVHighlights();
            }
        }
    }

    /// <summary>
    /// Gets a string representation of fixed IVs
    /// </summary>
    private static string GetFixedIVString(IndividualValueSet ivs)
    {
        var parts = new List<string>();
        if (ivs.HP != -1) parts.Add($"HP:{ivs.HP}");
        if (ivs.ATK != -1) parts.Add($"Atk:{ivs.ATK}");
        if (ivs.DEF != -1) parts.Add($"Def:{ivs.DEF}");
        if (ivs.SPA != -1) parts.Add($"SpA:{ivs.SPA}");
        if (ivs.SPD != -1) parts.Add($"SpD:{ivs.SPD}");
        if (ivs.SPE != -1) parts.Add($"Spe:{ivs.SPE}");
        return string.Join(" ", parts);
    }

    /// <summary>
    /// Resets constraint highlights on all controls
    /// </summary>
    private void ResetConstraintHighlights()
    {
        shinyCombo.BackColor = SystemColors.Window;
        genderCombo.BackColor = SystemColors.Window;
        natureCombo.BackColor = SystemColors.Window;
        abilityCombo.BackColor = SystemColors.Window;
        scaleMin.BackColor = SystemColors.Window;
        scaleMax.BackColor = SystemColors.Window;
        ResetIVHighlights();
    }

    /// <summary>
    /// Resets IV control highlights
    /// </summary>
    private void ResetIVHighlights()
    {
        ivHpMin.BackColor = ivHpMax.BackColor = SystemColors.Window;
        ivAtkMin.BackColor = ivAtkMax.BackColor = SystemColors.Window;
        ivDefMin.BackColor = ivDefMax.BackColor = SystemColors.Window;
        ivSpaMin.BackColor = ivSpaMax.BackColor = SystemColors.Window;
        ivSpdMin.BackColor = ivSpdMax.BackColor = SystemColors.Window;
        ivSpeMin.BackColor = ivSpeMax.BackColor = SystemColors.Window;
    }

    /// <summary>
    /// Checks if ability selection is valid for the encounter
    /// </summary>
    private static bool IsAbilitySelectionValid(AbilityPermission encounterAbility, AbilityPermission selectedAbility)
    {
        if (selectedAbility == AbilityPermission.Any12H) // "Any" selection
            return true;

        return (encounterAbility, selectedAbility) switch
        {
            (AbilityPermission.OnlyFirst, AbilityPermission.OnlyFirst) => true,
            (AbilityPermission.OnlySecond, AbilityPermission.OnlySecond) => true,
            (AbilityPermission.OnlyHidden, AbilityPermission.OnlyHidden) => true,
            (AbilityPermission.Any12, AbilityPermission.Any12) => true,
            (AbilityPermission.Any12, AbilityPermission.OnlyFirst) => true,
            (AbilityPermission.Any12, AbilityPermission.OnlySecond) => true,
            _ => false
        };
    }

    /// <summary>
    /// Gets display text for ability constraints
    /// </summary>
    private static string GetAbilityConstraintText(AbilityPermission ability) => ability switch
    {
        AbilityPermission.OnlyFirst => "Ability 1 only",
        AbilityPermission.OnlySecond => "Ability 2 only",
        AbilityPermission.OnlyHidden => "Hidden Ability only",
        AbilityPermission.Any12 => "Ability 1 or 2 only",
        _ => "Any"
    };

    /// <summary>
    /// Gets gender name for display
    /// </summary>
    private static string GetGenderName(byte gender) => gender switch
    {
        0 => "Male",
        1 => "Female",
        2 => "Genderless",
        _ => "Unknown"
    };

    /// <summary>
    /// Gets all encounters for a species (all forms)
    /// </summary>
    private static List<ITeraRaid9> GetAllEncountersForSpecies(IReadOnlyList<ITeraRaid9> encounters, int species)
    {
        return [.. encounters.Where(e => e.Species == species)];
    }

    /// <summary>
    /// Filters encounters by species and form
    /// </summary>
    private static List<ITeraRaid9> GetEncountersForSpecies(IReadOnlyList<ITeraRaid9> encounters, int species, byte form)
    {
        return [.. encounters.Where(e => e.Species == species && IsFormCompatible(e, species, form))];
    }

    /// <summary>
    /// Handles the search button click
    /// </summary>
    private async void SearchButton_Click(object? sender, EventArgs e)
    {
        if (_searchCts != null)
        {
            _searchCts.Cancel();
            return;
        }

        if (speciesCombo.SelectedValue is not int species)
        {
            WinFormsUtil.Alert("Please select a species!");
            return;
        }

        // Validate search criteria
        var validationErrors = ValidateSearchCriteria();
        if (validationErrors.Count > 0)
        {
            var message = "The following search criteria will never find results:\n\n" + string.Join("\n", validationErrors);
            WinFormsUtil.Alert(message);
            return;
        }

        var selectedEncounter = (encounterCombo.SelectedItem as EncounterItem)?.Encounter;
        // Use form from selected encounter if specific encounter is selected, otherwise search all forms (0)
        var form = selectedEncounter?.Form ?? 0;
        var criteria = GetCriteria();

        _results.Clear();
        resultsGrid.Rows.Clear();
        _gridRowToResult.Clear();

        // Hide preview panel when starting new search
        if (_previewPanel != null && _previewPanel.Visible)
        {
            _previewPanel.Visible = false;
            resultsGrid.Height = resultsPanel.Height;
        }

        searchButton.Text = "Stop";
        progressBar.Visible = true;
        statusLabel.Text = "Searching...";

        _searchCts = new CancellationTokenSource();

        try
        {
            await Task.Run(() => SearchSeeds(species, form, criteria, selectedEncounter, _searchCts.Token));
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed && !statusLabel.IsDisposed)
                statusLabel.Text = "Search cancelled";
        }
        finally
        {
            if (!IsDisposed)
            {
                if (!searchButton.IsDisposed)
                    searchButton.Text = "Search";
                if (!progressBar.IsDisposed)
                    progressBar.Visible = false;
            }
            _searchCts?.Dispose();
            _searchCts = null;
        }
    }

    /// <summary>
    /// Validates search criteria before starting search
    /// </summary>
    private List<string> ValidateSearchCriteria()
    {
        var errors = new List<string>();
        var selectedEncounter = (encounterCombo.SelectedItem as EncounterItem)?.Encounter;

        if (selectedEncounter == null)
            return errors; // No specific encounter selected, all criteria are potentially valid

        // Check shiny
        if (selectedEncounter.Shiny == Shiny.Never && shinyCombo.SelectedIndex == 2) // Always shiny selected
        {
            errors.Add("• This encounter is never shiny, but you selected 'Always' shiny");
        }
        else if (selectedEncounter.Shiny == Shiny.Always && shinyCombo.SelectedIndex == 1) // Never shiny selected
        {
            errors.Add("• This encounter is always shiny, but you selected 'Never' shiny");
        }

        // Check gender
        if (selectedEncounter.Gender != FixedGenderUtil.GenderRandom && genderCombo.SelectedIndex != 0)
        {
            var encounterGender = GetGenderName(selectedEncounter.Gender);
            var selectedGender = genderCombo.SelectedIndex switch
            {
                1 => "Male",
                2 => "Female",
                3 => "Genderless",
                _ => "Unknown"
            };

            if (encounterGender != selectedGender)
            {
                errors.Add($"• This encounter is always {encounterGender}, but you selected {selectedGender}");
            }
        }

        // Check nature
        if (selectedEncounter is IFixedNature fn && fn.Nature != Nature.Random && natureCombo.SelectedIndex != 0)
        {
            var selectedNature = (Nature)(natureCombo.SelectedIndex - 1);
            if (fn.Nature != selectedNature)
            {
                errors.Add($"• This encounter always has {fn.Nature} nature, but you selected {selectedNature}");
            }
        }

        // Check IVs
        if (selectedEncounter is EncounterMight9 might)
        {
            ValidateFixedIVSearch(might.IVs, errors);
            ValidateFlawlessIVSearch(6, errors);
        }
        else if (selectedEncounter is EncounterDist9 dist)
        {
            if (dist.IVs.IsSpecified)
            {
                ValidateFixedIVSearch(dist.IVs, errors);
            }
            else if (dist.FlawlessIVCount > 0)
            {
                ValidateFlawlessIVSearch(dist.FlawlessIVCount, errors);
            }
        }
        else if (selectedEncounter is EncounterTera9 tera && tera.FlawlessIVCount > 0)
        {
            ValidateFlawlessIVSearch(tera.FlawlessIVCount, errors);
        }

        // Check ability
        if (selectedEncounter.Ability != AbilityPermission.Any12H && abilityCombo.SelectedIndex != 0)
        {
            var selectedAbility = GetAbilityPermission();
            if (!IsAbilitySelectionValid(selectedEncounter.Ability, selectedAbility))
            {
                errors.Add($"• This encounter has {GetAbilityConstraintText(selectedEncounter.Ability)}, but your selection is incompatible");
            }
        }

        // Check scale constraints
        ValidateScaleSearch(selectedEncounter, errors);

        return errors;
    }

    /// <summary>
    /// Validates search criteria against scale constraints
    /// </summary>
    private void ValidateScaleSearch(ITeraRaid9 selectedEncounter, List<string> errors)
    {
        // Extract scale type and value based on encounter type
        SizeType9? scaleType = null;
        byte? fixedScale = null;
        
        if (selectedEncounter is EncounterMight9 might)
        {
            scaleType = might.ScaleType;
            if (might.ScaleType == SizeType9.VALUE)
                fixedScale = might.Scale;
        }
        else if (selectedEncounter is EncounterDist9 dist)
        {
            scaleType = dist.ScaleType;
            if (dist.ScaleType == SizeType9.VALUE)
                fixedScale = dist.Scale;
        }
        
        // If there's no scale constraint, nothing to validate
        if (scaleType == null || scaleType == SizeType9.RANDOM)
            return;
        
        var minValue = (byte)scaleMin.Value;
        var maxValue = (byte)scaleMax.Value;
        
        if (scaleType == SizeType9.VALUE && fixedScale.HasValue)
        {
            // Fixed scale value - check if the range includes it
            if (minValue > fixedScale.Value || maxValue < fixedScale.Value)
            {
                errors.Add($"• This encounter always has scale {fixedScale.Value}, but your range is {minValue}-{maxValue}");
            }
        }
        else if (scaleType != SizeType9.RANDOM)
        {
            // Size category constraint - check if range overlaps with the category
            var (categoryMin, categoryMax) = GetScaleRange(scaleType.Value);
            
            // Check if there's any overlap between user range and category range
            if (maxValue < categoryMin || minValue > categoryMax)
            {
                var typeName = GetScaleTypeName(scaleType.Value);
                errors.Add($"• This encounter always has {typeName} scale ({categoryMin}-{categoryMax}), but your range is {minValue}-{maxValue}");
            }
        }
    }

    /// <summary>
    /// Validates search criteria against fixed IVs
    /// </summary>
    private void ValidateFixedIVSearch(IndividualValueSet fixedIVs, List<string> errors)
    {
        var checks = new[]
        {
            (fixedIVs.HP, ivHpMin.Value, ivHpMax.Value, "HP"),
            (fixedIVs.ATK, ivAtkMin.Value, ivAtkMax.Value, "Attack"),
            (fixedIVs.DEF, ivDefMin.Value, ivDefMax.Value, "Defense"),
            (fixedIVs.SPA, ivSpaMin.Value, ivSpaMax.Value, "Sp. Attack"),
            (fixedIVs.SPD, ivSpdMin.Value, ivSpdMax.Value, "Sp. Defense"),
            (fixedIVs.SPE, ivSpeMin.Value, ivSpeMax.Value, "Speed"),
        };

        foreach (var (fixedValue, min, max, statName) in checks)
        {
            if (fixedValue != -1 && (min > fixedValue || max < fixedValue))
            {
                errors.Add($"• {statName} is fixed to {fixedValue}, but your range is {min}-{max}");
            }
        }
    }

    /// <summary>
    /// Validates search criteria against flawless IV count
    /// </summary>
    private void ValidateFlawlessIVSearch(byte flawlessCount, List<string> errors)
    {
        var maxValues = new[]
        {
            (ivHpMax.Value, "HP"),
            (ivAtkMax.Value, "Attack"),
            (ivDefMax.Value, "Defense"),
            (ivSpaMax.Value, "Sp. Attack"),
            (ivSpdMax.Value, "Sp. Defense"),
            (ivSpeMax.Value, "Speed"),
        };

        var possibleFlawlessSlots = maxValues.Count(v => v.Value >= 31);

        if (possibleFlawlessSlots < flawlessCount)
        {
            var excludedStats = maxValues.Where(v => v.Value < 31).Select(v => v.Item2).ToList();
            errors.Add($"• This encounter has {flawlessCount} guaranteed perfect IVs, but you excluded 31 from: {string.Join(", ", excludedStats)}");
        }
    }

    /// <summary>
    /// Represents an IV range for searching
    /// </summary>
    private readonly record struct IVRange(int Min, int Max);

    /// <summary>
    /// Gets the search criteria from the form controls
    /// </summary>
    private EncounterCriteria GetCriteria()
    {
        var genderIndex = genderCombo.SelectedIndex;
        var gender = genderIndex switch
        {
            0 => Gender.Random,
            1 => (Gender)0, // Male
            2 => (Gender)1, // Female
            3 => (Gender)2, // Genderless
            _ => Gender.Random
        };

        var criteria = new EncounterCriteria
        {
            Gender = gender,
            Ability = GetAbilityPermission(),
            Nature = natureCombo.SelectedIndex == 0 ? Nature.Random : (Nature)(natureCombo.SelectedIndex - 1),
            Shiny = (Shiny)shinyCombo.SelectedIndex,
        };

        return criteria;
    }

    /// <summary>
    /// Gets the IV ranges from the form controls
    /// </summary>
    private IVRange[] GetIVRanges()
    {
        return
        [
            new IVRange((int)ivHpMin.Value, (int)ivHpMax.Value),
            new IVRange((int)ivAtkMin.Value, (int)ivAtkMax.Value),
            new IVRange((int)ivDefMin.Value, (int)ivDefMax.Value),
            new IVRange((int)ivSpaMin.Value, (int)ivSpaMax.Value),
            new IVRange((int)ivSpdMin.Value, (int)ivSpdMax.Value),
            new IVRange((int)ivSpeMin.Value, (int)ivSpeMax.Value),
        ];
    }

    /// <summary>
    /// Gets the ability permission from the form control
    /// </summary>
    private AbilityPermission GetAbilityPermission()
    {
        return abilityCombo.SelectedIndex switch
        {
            0 => AbilityPermission.Any12H,
            1 => AbilityPermission.OnlyFirst,
            2 => AbilityPermission.OnlySecond,
            3 => AbilityPermission.OnlyHidden,
            4 => AbilityPermission.Any12,
            _ => AbilityPermission.Any12H
        };
    }

    /// <summary>
    /// Searches for seeds that match the criteria
    /// </summary>
    private void SearchSeeds(int species, byte form, EncounterCriteria criteria, ITeraRaid9? specificEncounter, CancellationToken token)
    {
        var maxResults = (int)maxSeedsNum.Value;
        var ivRanges = GetIVRanges();

        // Parse seed range from UI
        uint startSeed = 0x00000000;
        uint endSeed = 0xFFFFFFFF;

        if (!string.IsNullOrEmpty(startSeedTextBox?.Text))
        {
            if (uint.TryParse(startSeedTextBox.Text, System.Globalization.NumberStyles.HexNumber, null, out uint parsedStart))
            {
                startSeed = parsedStart;
            }
            else
            {
                this.Invoke(() => WinFormsUtil.Alert("Invalid start seed format. Using default 00000000."));
            }
        }

        if (!string.IsNullOrEmpty(endSeedTextBox?.Text))
        {
            if (uint.TryParse(endSeedTextBox.Text, System.Globalization.NumberStyles.HexNumber, null, out uint parsedEnd))
            {
                endSeed = parsedEnd;
            }
            else
            {
                this.Invoke(() => WinFormsUtil.Alert("Invalid end seed format. Using default FFFFFFFF."));
            }
        }

        if (startSeed > endSeed)
        {
            this.Invoke(() => WinFormsUtil.Alert("Start seed must be less than or equal to end seed!"));
            return;
        }

        // Use parallel processing with all available CPU cores
        SearchSeedsParallel(species, form, criteria, specificEncounter, startSeed, endSeed, maxResults, ivRanges, token);
    }

    /// <summary>
    /// Parallel implementation of seed searching using all CPU cores
    /// </summary>
    private void SearchSeedsParallel(int species, byte form, EncounterCriteria criteria, ITeraRaid9? specificEncounter,
        uint startSeed, uint endSeed, int maxResults, IVRange[] ivRanges, CancellationToken token)
    {
        var results = new ConcurrentBag<SeedResult>();
        var resultsLock = new object();
        var orderedResults = new List<SeedResult>();
        var gridRowToResultMap = new Dictionary<int, SeedResult>(); // Map grid row index to result
        
        long totalSeeds = (long)endSeed - startSeed + 1;
        long seedsChecked = 0;
        long lastProgressUpdate = 0;
        var foundEnough = false;

        // Determine optimal chunk size based on CPU cores
        int coreCount = Environment.ProcessorCount;
        int chunkSize = Math.Max(10000, (int)(totalSeeds / (coreCount * 10))); // At least 10000 seeds per chunk
        
        // Create parallel options with cancellation token
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = token,
            MaxDegreeOfParallelism = coreCount
        };

        try
        {
            // Process seeds in parallel chunks
            Parallel.For(0, (totalSeeds + chunkSize - 1) / chunkSize, parallelOptions, chunkIndex =>
            {
                // Calculate chunk boundaries
                uint chunkStart = (uint)(startSeed + chunkIndex * chunkSize);
                uint chunkEnd = Math.Min(endSeed, (uint)(chunkStart + chunkSize - 1));
                
                // Process chunk
                for (uint seed = chunkStart; seed <= chunkEnd && !foundEnough && !token.IsCancellationRequested; seed++)
                {
                    // Increment seeds checked atomically
                    var currentChecked = Interlocked.Increment(ref seedsChecked);
                    
                    // Update progress periodically (less frequently for parallel to avoid contention)
                    if (currentChecked - Interlocked.Read(ref lastProgressUpdate) >= 50000)
                    {
                        Interlocked.Exchange(ref lastProgressUpdate, currentChecked);
                        try
                        {
                            this.Invoke(() =>
                            {
                                if (!IsDisposed && !progressBar.IsDisposed && !statusLabel.IsDisposed)
                                {
                                    var progressPercent = (int)((currentChecked / (double)totalSeeds) * 100);
                                    progressBar.Value = Math.Min(progressPercent, 100);
                                    statusLabel.Text = $"Using {coreCount} cores | Checked {currentChecked:N0} ({progressPercent:F2}%), found {results.Count}";
                                }
                            });
                        }
                        catch (ObjectDisposedException)
                        {
                            // Form was disposed while updating, ignore
                        }
                    }

                    // Check if this seed produces a matching encounter
                    ITeraRaid9? encounter;
                    if (specificEncounter != null)
                    {
                        // If user selected a specific encounter, check if it's form-compatible
                        if (!IsFormCompatible(specificEncounter, species, form))
                            continue;

                        // Check if this seed can generate this encounter
                        if (!specificEncounter.CanBeEncountered(seed))
                            continue;

                        encounter = specificEncounter;
                    }
                    else
                    {
                        // Find any matching encounter for this species/form
                        encounter = FindMatchingEncounter(seed, form);
                        if (encounter == null)
                            continue;
                    }

                    // Create the generation parameters
                    var pi = PersonalTable.SV[encounter.Species, encounter.Form];
                    var genderRatio = pi.Gender;
                    byte height = 0;
                    byte weight = 0;
                    SizeType9 scaleType = SizeType9.RANDOM;
                    byte scale = 0;
                    IndividualValueSet ivs = default;
                    
                    // Extract encounter-specific properties
                    if (encounter is EncounterMight9 might)
                    {
                        genderRatio = might.Gender switch
                        {
                            0 => PersonalInfo.RatioMagicMale,
                            1 => PersonalInfo.RatioMagicFemale,
                            2 => PersonalInfo.RatioMagicGenderless,
                            _ => pi.Gender
                        };
                        scaleType = might.ScaleType;
                        scale = might.Scale;
                        ivs = might.IVs;
                    }
                    else if (encounter is EncounterDist9 dist)
                    {
                        scaleType = dist.ScaleType;
                        scale = dist.Scale;
                        ivs = dist.IVs;
                    }
            
                    var param = new GenerateParam9(
                        encounter.Species,
                        genderRatio,
                        encounter.FlawlessIVCount,
                        1, // roll count - PKHeX uses 1 for all raid types
                        height,
                        weight,
                        scaleType,
                        scale,
                        encounter.Ability,
                        encounter.Shiny,
                        encounter is IFixedNature fn1 ? fn1.Nature : Nature.Random,
                        ivs
                    );
                    
                    // Get TID/SID for shiny check
                    // Convert from display format (TID7/SID7) to ID32, then to 16-bit values
                    uint id32 = ((uint)sidNum.Value * 1000000) + (uint)tidNum.Value;
                    ushort tid = (ushort)(id32 & 0xFFFF);
                    ushort sid = (ushort)(id32 >> 16);
                    
                    // Check if this seed will produce a shiny
                    bool willBeShiny = WillBeShiny(seed, param, id32);
                    
                    // If we're searching for shiny only and this won't be shiny, skip early
                    if (criteria.Shiny == Shiny.Always && !willBeShiny)
                        continue;
                    
                    // If we're searching for non-shiny only and this will be shiny, skip early
                    if (criteria.Shiny == Shiny.Never && willBeShiny)
                        continue;
            
                    // For Shiny.Random encounters, adjust criteria to match what will actually be generated
                    var adjustedCriteria = criteria;
                    if (param.Shiny == Shiny.Random)
                    {
                        adjustedCriteria = new EncounterCriteria
                        {
                            Gender = criteria.Gender,
                            Ability = criteria.Ability,
                            Nature = criteria.Nature,
                            Shiny = willBeShiny ? Shiny.Always : Shiny.Never
                        };
                    }
                    
                    // Generate a Pokemon from this seed
                    var pk = GenerateRaidPokemon(encounter, seed, adjustedCriteria, form);
                    if (pk == null)
                        continue;

                    // For random form encounters, verify the generated form matches what the user is searching for
                    if (encounter is IEncounterFormRandom { IsRandomUnspecificForm: true } && pk.Form != form)
                        continue;

                    bool matchesShiny = criteria.Shiny switch
                    {
                        Shiny.Never => !pk.IsShiny,
                        Shiny.Always => pk.IsShiny,
                        Shiny.AlwaysSquare => pk.IsShiny && pk.ShinyXor == 0,
                        Shiny.AlwaysStar => pk.IsShiny && pk.ShinyXor != 0,
                        _ => true // Random accepts any
                    };

                    if (!matchesShiny)
                        continue;

                    // Check other criteria
                    if (criteria.Gender != Gender.Random && pk.Gender != (int)criteria.Gender)
                        continue;

                    if (criteria.Nature != Nature.Random && pk.Nature != criteria.Nature)
                        continue;

                    if (!CheckIVRanges(pk, ivRanges))
                        continue;

                    // Check ability if specified
                    if (criteria.Ability != AbilityPermission.Any12H && !CheckAbilityCriteria(pk, criteria.Ability))
                        continue;

                    // Check Scale filter (Gen9 specific)
                    var scaleMin = (byte)this.scaleMin.Value;
                    var scaleMax = (byte)this.scaleMax.Value;
                    if (pk.Scale < scaleMin || pk.Scale > scaleMax)
                        continue;

                    // Add to results
                    var result = new SeedResult
                    {
                        Seed = seed,
                        Encounter = encounter,
                        Pokemon = pk
                    };

                    results.Add(result);
                    
                    // Check if we've found enough results
                    lock (resultsLock)
                    {
                        if (results.Count >= maxResults)
                        {
                            foundEnough = true;
                        }
                    }
                    
                    // Add to grid in real-time for immediate feedback
                    AddResultToGrid(result);

                    // Break early if we're at the last seed to avoid overflow
                    if (seed == chunkEnd)
                        break;
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Search was cancelled
        }

        // Sort results by seed for the _results list (used for export)
        orderedResults = [.. results.OrderBy(r => r.Seed).Take(maxResults)];
        _results = orderedResults;

        // Update final status
        try
        {
            this.Invoke(() =>
            {
                if (!IsDisposed && !statusLabel.IsDisposed && !progressBar.IsDisposed)
                {
                    statusLabel.Text = $"Found {orderedResults.Count} matches after checking {seedsChecked:N0} seeds (used {coreCount} cores)";
                    progressBar.Value = 100;
                }
            });
        }
        catch (ObjectDisposedException)
        {
            // Form was disposed while updating, ignore
        }
    }

    /// <summary>
    /// Adds a result directly to the grid without invoking (for use when already on UI thread)
    /// </summary>
    private int AddResultToGridDirect(SeedResult result)
    {
        var row = resultsGrid.Rows.Add(
            $"{result.Seed:X8}",
            $"{result.Encounter.Stars}★",
            result.Pokemon.IsShiny ? "★" : "",
            result.Pokemon.Nature.ToString(),
            GetAbilityName(result.Pokemon),
            GetIVString(result.Pokemon),
            result.Pokemon.TeraTypeOriginal.ToString(),
            $"{result.Pokemon.Scale}"
        );
        
        _gridRowToResult[row] = result; // Store the mapping

        if (result.Pokemon.IsShiny)
        {
            // Use a darker gold color that works better with dark themes
            resultsGrid.Rows[row].DefaultCellStyle.BackColor = Color.FromArgb(64, 64, 32);
            resultsGrid.Rows[row].DefaultCellStyle.ForeColor = Color.Gold;

            // Ensure the selection colors are still visible for shiny rows
            resultsGrid.Rows[row].DefaultCellStyle.SelectionBackColor = Color.DarkGoldenrod;
            resultsGrid.Rows[row].DefaultCellStyle.SelectionForeColor = Color.White;
        }
        
        return row;
    }

    /// <summary>
    /// Checks if the Pokemon matches the ability criteria
    /// </summary>
    private static bool CheckAbilityCriteria(PK9 pk, AbilityPermission criteria)
    {
        var pi = PersonalTable.SV[pk.Species, pk.Form];

        return (criteria, pk.AbilityNumber) switch
        {
            (AbilityPermission.OnlyFirst, 1) => pk.Ability == pi.Ability1,
            (AbilityPermission.OnlySecond, 2) => pk.Ability == pi.Ability2,
            (AbilityPermission.OnlyHidden, 4) => pk.Ability == pi.AbilityH,
            (AbilityPermission.Any12, <= 2) => true,
            (_, _) when criteria == AbilityPermission.Any12H => true,
            _ => false
        };
    }

    /// <summary>
    /// Checks if the Pokemon's IVs are within the specified ranges
    /// </summary>
    private static bool CheckIVRanges(PK9 pk, IVRange[] ranges)
    {
        return pk.IV_HP >= ranges[0].Min && pk.IV_HP <= ranges[0].Max &&
               pk.IV_ATK >= ranges[1].Min && pk.IV_ATK <= ranges[1].Max &&
               pk.IV_DEF >= ranges[2].Min && pk.IV_DEF <= ranges[2].Max &&
               pk.IV_SPA >= ranges[3].Min && pk.IV_SPA <= ranges[3].Max &&
               pk.IV_SPD >= ranges[4].Min && pk.IV_SPD <= ranges[4].Max &&
               pk.IV_SPE >= ranges[5].Min && pk.IV_SPE <= ranges[5].Max;
    }

    /// <summary>
    /// Checks if an encounter is compatible with the desired form
    /// </summary>
    private static bool IsFormCompatible(ITeraRaid9 encounter, int species, byte form)
    {
        // Check if the encounter matches the species
        if (encounter.Species != species)
            return false;

        // Check if forms match or if the encounter has a random form
        if (encounter.Form == form)
            return true;

        // Check for random form encounters
        if (encounter is IEncounterFormRandom efr && efr.IsRandomUnspecificForm)
            return true;

        // Check if form can change between the encounter form and desired form
        return FormInfo.IsFormChangeable((ushort)species, encounter.Form, form, EntityContext.Gen9, EntityContext.Gen9);
    }

    /// <summary>
    /// Finds a matching encounter for the given seed
    /// </summary>
    private ITeraRaid9? FindMatchingEncounter(uint seed, byte form)
    {
        // Use cached encounters filtered by form - only searches encounters that exist for this species
        var formEncounters = _cachedSpeciesEncounters.Where(e =>
            e.Form == form || e.Form >= EncounterUtil.FormDynamic);

        foreach (var encounter in formEncounters)
        {
            if (encounter.CanBeEncountered(seed))
                return encounter;
        }

        return null;
    }

    /// <summary>
    /// Pre-checks if a seed will produce a shiny Pokemon for the given encounter parameters
    /// </summary>
    private bool WillBeShiny(uint seed, GenerateParam9 param, uint id32)
    {
        // Only relevant for Shiny.Random encounters
        if (param.Shiny != Shiny.Random)
            return param.Shiny == Shiny.Always;

        var rand = new Xoroshiro128Plus(seed);
        _ = rand.NextInt(); // EC
        var fakeTID = (uint)rand.NextInt();
        uint pid = (uint)rand.NextInt();
        
        // Check if it rolls shiny within the allowed roll count
        for (int i = 1; i <= param.RollCount; i++)
        {
            var xor = ShinyUtil.GetShinyXor(pid, fakeTID);
            if (xor < 16) // Is shiny
                return true;
            if (i < param.RollCount)
                pid = (uint)rand.NextInt();
        }
        return false;
    }

    /// <summary>
    /// Generates a Pokemon from the encounter and seed using the exact raid generation method
    /// </summary>
    private PK9? GenerateRaidPokemon(ITeraRaid9 encounter, uint seed, EncounterCriteria criteria, byte desiredForm)
    {
        var pi = PersonalTable.SV[encounter.Species, encounter.Form];

        // Get TID/SID from UI instead of save file
        // Convert from display format (TID7/SID7) to ID32
        uint id32 = ((uint)sidNum.Value * 1000000) + (uint)tidNum.Value;

        // Get proper parameters based on encounter type
        byte genderRatio = pi.Gender;
        byte height = 0;
        byte weight = 0;
        SizeType9 scaleType = SizeType9.RANDOM;
        byte scale = 0;
        IndividualValueSet ivs = default;

        // Extract encounter-specific properties
        if (encounter is EncounterMight9 might)
        {
            genderRatio = might.Gender switch
            {
                0 => PersonalInfo.RatioMagicMale,
                1 => PersonalInfo.RatioMagicFemale,
                2 => PersonalInfo.RatioMagicGenderless,
                _ => pi.Gender
            };
            scaleType = might.ScaleType;
            scale = might.Scale;
            ivs = might.IVs;
        }
        else if (encounter is EncounterDist9 dist)
        {
            scaleType = dist.ScaleType;
            scale = dist.Scale;
            ivs = dist.IVs;
        }

        var param = new GenerateParam9(
            encounter.Species,
            genderRatio,
            encounter.FlawlessIVCount,
            1, // roll count - PKHeX uses 1 for all raid types
            height,
            weight,
            scaleType,
            scale,
            encounter.Ability,
            encounter.Shiny,
            encounter is IFixedNature fn ? fn.Nature : Nature.Random,
            ivs
        );

        int language = (int)Language.GetSafeLanguage789((LanguageID)_saveFileEditor.SAV.Language);

        var pk = new PK9
        {
            Species = encounter.Species,
            Form = encounter.Form < EncounterUtil.FormDynamic ? encounter.Form : desiredForm,
            CurrentLevel = encounter.LevelMin,
            MetLocation = Locations.TeraCavern9,
            MetLevel = encounter.LevelMin,
            MetDate = EncounterDate.GetDateSwitch(),
            Version = _saveFileEditor.SAV.Version,
            Ball = (byte)Ball.Poke,
            ID32 = id32,
            OriginalTrainerName = _saveFileEditor.SAV.OT,
            OriginalTrainerGender = _saveFileEditor.SAV.Gender,
            Language = language,
            ObedienceLevel = encounter.LevelMin,
            OriginalTrainerFriendship = pi.BaseFriendship,
            Nickname = SpeciesName.GetSpeciesNameGeneration(encounter.Species, language, 9),
        };

        try
        {
            // Use the exact PKHeX method for generating raid data
            if (!Encounter9RNG.GenerateData(pk, param, criteria, seed))
                return null;

            var teraType = Tera9RNG.GetTeraType(seed, encounter.TeraType, encounter.Species, encounter.Form);
            pk.TeraTypeOriginal = (MoveType)teraType;

            if (encounter is IMoveset ms && ms.Moves.HasMoves)
                pk.SetMoves(ms.Moves);

            // For 7-star raids, set the Mightiest Mark
            if (encounter is EncounterMight9)
            {
                pk.RibbonMarkMightiest = true;
                pk.AffixedRibbon = (sbyte)RibbonIndex.MarkMightiest;
            }

            // Ensure valid Met Date for Mighty Raid Pokemon
            pk.CheckAndSetUnrivaledDate();

            pk.ResetPartyStats();

            return pk;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error generating raid Pokémon: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Adds a result to the data grid
    /// </summary>
    private void AddResultToGrid(SeedResult result)
    {
        try
        {
            this.Invoke(() =>
            {
                if (!IsDisposed && !resultsGrid.IsDisposed)
                {
                    var row = resultsGrid.Rows.Add(
                        $"{result.Seed:X8}",
                        $"{result.Encounter.Stars}★",
                        result.Pokemon.IsShiny ? "★" : "",
                        result.Pokemon.Nature.ToString(),
                        GetAbilityName(result.Pokemon),
                        GetIVString(result.Pokemon),
                        result.Pokemon.TeraTypeOriginal.ToString(),
                        $"{result.Pokemon.Scale}"
                    );

                    _gridRowToResult[row] = result; // Store the mapping

                    if (result.Pokemon.IsShiny)
                    {
                        // Use a darker gold color that works better with dark themes
                        resultsGrid.Rows[row].DefaultCellStyle.BackColor = Color.FromArgb(64, 64, 32);
                        resultsGrid.Rows[row].DefaultCellStyle.ForeColor = Color.Gold;

                        // Ensure the selection colors are still visible for shiny rows
                        resultsGrid.Rows[row].DefaultCellStyle.SelectionBackColor = Color.DarkGoldenrod;
                        resultsGrid.Rows[row].DefaultCellStyle.SelectionForeColor = Color.White;
                    }
                }
            });
        }
        catch (ObjectDisposedException)
        {
            // Form was disposed while updating, ignore
        }
    }

    /// <summary>
    /// Gets the ability name for the Pokemon
    /// </summary>
    private static string GetAbilityName(PK9 pk)
    {
        var abilities = PersonalTable.SV[pk.Species, pk.Form];
        return pk.AbilityNumber switch
        {
            1 => GameInfo.Strings.abilitylist[abilities.Ability1],
            2 => GameInfo.Strings.abilitylist[abilities.Ability2],
            4 => GameInfo.Strings.abilitylist[abilities.AbilityH],
            _ => "?"
        };
    }

    /// <summary>
    /// Gets the IV string representation
    /// </summary>
    private static string GetIVString(PK9 pk)
    {
        return $"{pk.IV_HP}/{pk.IV_ATK}/{pk.IV_DEF}/{pk.IV_SPA}/{pk.IV_SPD}/{pk.IV_SPE}";
    }

    /// <summary>
    /// Handles double-clicking a result in the grid
    /// </summary>
    private void ResultsGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || !_gridRowToResult.TryGetValue(e.RowIndex, out var result))
            return;
        _pkmEditor.PopulateFields(result.Pokemon);

        WinFormsUtil.Alert($"Loaded {result.Pokemon.Nickname}!\nSeed: {result.Seed:X8}");
    }

    /// <summary>
    /// Handles exporting results to CSV
    /// </summary>
    private void ExportButton_Click(object? sender, EventArgs e)
    {
        if (_results.Count == 0)
        {
            WinFormsUtil.Alert("No results to export!");
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = $"Gen9_Seeds_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        if (sfd.ShowDialog() != DialogResult.OK)
            return;

        try
        {
            using var writer = new System.IO.StreamWriter(sfd.FileName);
            writer.WriteLine($"Seed,Stars,Shiny,Nature,Ability,IVs,TeraType,Scale,TID,SID");

            foreach (var result in _results)
            {
                // Convert to display format (Gen 7+ format)
                uint pokemonId32 = ((uint)result.Pokemon.SID16 << 16) | result.Pokemon.TID16;
                uint displayTID = pokemonId32 % 1000000;
                uint displaySID = pokemonId32 / 1000000;

                writer.WriteLine($"{result.Seed:X8},{result.Encounter.Stars}★,{(result.Pokemon.IsShiny ? "Yes" : "No")}," +
                               $"{result.Pokemon.Nature},{GetAbilityName(result.Pokemon)},{GetIVString(result.Pokemon)}," +
                               $"{result.Pokemon.TeraTypeOriginal},{result.Pokemon.Scale},{displayTID},{displaySID}");
            }

            WinFormsUtil.Alert("Export successful!");
        }
        catch (Exception ex)
        {
            WinFormsUtil.Error($"Export failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Represents a seed search result
    /// </summary>
    private sealed class SeedResult
    {
        public required uint Seed { get; init; }
        public required ITeraRaid9 Encounter { get; init; }
        public required PK9 Pokemon { get; init; }
    }

    /// <summary>
    /// Combo box item for display
    /// </summary>
    private sealed class ComboItem(string text, int value)
    {
        public string Text { get; } = text;
        public int Value { get; } = value;
    }

    /// <summary>
    /// Encounter item for display
    /// </summary>
    private sealed class EncounterItem(ITeraRaid9? encounter)
    {
        public string Text { get; } = encounter == null ? "Any Encounter" : GetEncounterText(encounter);
        public ITeraRaid9? Encounter { get; } = encounter;

        private static string GetEncounterText(ITeraRaid9 encounter)
        {
            var type = GetEncounterType(encounter);
            var formName = "";

            // Always show form name if it's not the base form
            if (encounter.Form != 0)
            {
                var forms = FormConverter.GetFormList((ushort)encounter.Species, GameInfo.Strings.types,
                    GameInfo.Strings.forms, GameInfo.GenderSymbolASCII, EntityContext.Gen9);
                if (encounter.Form < forms.Length)
                {
                    var form = forms[encounter.Form];
                    // Clean up form name if needed
                    if (!string.IsNullOrEmpty(form) && form != "-")
                        formName = $" ({form})";
                }
            }
            // For random form encounters, indicate it
            else if (encounter is IEncounterFormRandom { IsRandomUnspecificForm: true })
            {
                formName = " (Random Form)";
            }

            return $"{encounter.Stars}★ {type}{formName}";
        }

        /// <summary>
        /// Gets the encounter type display name
        /// </summary>
        private static string GetEncounterType(ITeraRaid9 encounter)
        {
            return encounter switch
            {
                EncounterTera9 => "Base",
                EncounterDist9 => "Event",
                EncounterMight9 => "Mighty",
                _ => "Unknown"
            };
        }
    }
}
