using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using Google.Android.Material.Button;
using Uabea.Core;
using Uabea.Formats;
using Uabea.Testing;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Uri = Android.Net.Uri;
using AlertDialog = AndroidX.AppCompat.App.AlertDialog;
using Path = System.IO.Path;
using OperationCanceledException = System.OperationCanceledException;

namespace Uabea.Android;

[Activity(Label = "UABEA Android", MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AppCompatActivity
{
    const int PickOpen = 1, PickImport = 2, PickResource = 3, PickSave = 4;
    UnityDocument? document;
    IReadOnlyList<UnityObject> objects = [];
    List<UnityObject> visible = [];
    List<BundleEntry> entries = [];
    readonly HashSet<string> selected = [];
    readonly List<string> log = [];
    string work = "", db = "", displayName = "", page = "Objetos";
    string? pendingExport, lastSaved;
    Uri? sourceUri;
    Func<string, CancellationToken, Task>? importAction;
    CancellationTokenSource? operation;
    LinearLayout root = null!, content = null!;
    TextView status = null!, summary = null!;
    ProgressBar progress = null!;
    EditText search = null!;
    Spinner filter = null!;
    ListView list = null!;
    bool busy;
    SafStorage saf = null!;
    readonly int[] classes = [0, 21, 48, 200, 28, 49, 114];

    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        work = Path.Combine(FilesDir!.AbsolutePath, "workspace"); Directory.CreateDirectory(work);
        db = Path.Combine(FilesDir.AbsolutePath, "classdata.tpk");
        saf = new(this, work);
        BuildUi();
        bool selfTest = Intent?.GetBooleanExtra("run_self_test", false) == true;
        global::Android.Util.Log.Info("UabeaSelfTest", $"Activity initialized; requested={selfTest}");
        try
        {
            global::Android.Util.Log.Info("UabeaSelfTest", "Preparing class database synchronously");
            ClassDatabaseInstaller.EnsureSync(Assets!, db, CancellationToken.None);
            global::Android.Util.Log.Info("UabeaSelfTest", $"Class database ready: {new FileInfo(db).Length} bytes");
            if (selfTest)
                _ = Run(token => AndroidSelfTest.Run(this, work, db, token));
        }
        catch (Exception e)
        {
            global::Android.Util.Log.Error("UabeaSelfTest", "Startup failed: " + e);
            status.Text = "Error al preparar classdata.tpk: " + e.Message;
            if (selfTest)
            {
                string report = JsonSerializer.Serialize(new { passed = false, stage = "classdata", error = e.ToString() });
                File.WriteAllText(Path.Combine(FilesDir!.AbsolutePath, "selftest.json"), report);
            }
        }
    }

    int Dp(int n) => (int)(n * Resources!.DisplayMetrics!.Density);
    TextView Text(string value, float size = 14)
    {
        var view = new TextView(this) { Text = value, TextSize = size };
        view.SetPadding(Dp(4), Dp(6), Dp(4), Dp(6)); return view;
    }
    MaterialButton Button(string text, Action click)
    {
        var button = new MaterialButton(this) { Text = text, TextSize = 12 };
        button.SetAllCaps(false); button.Click += (_, _) => { if (!busy) click(); }; return button;
    }
    void Row(LinearLayout parent, params View[] children)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        foreach (var child in children) row.AddView(child);
        var scroll = new HorizontalScrollView(this); scroll.AddView(row); parent.AddView(scroll);
    }
    void BuildUi()
    {
        root = new(this) { Orientation = Orientation.Vertical };
        root.SetPadding(Dp(16), Dp(32), Dp(16), Dp(24));
        root.AddView(Text("UABEA Android", 26));
        summary = Text("Editor Unity · 0.2.0 alpha", 13); root.AddView(summary);
        var tools = Button("Herramientas", () => { });
        tools.Click += (_, _) => { if (!busy) ShowTools(tools); };
        Row(root, Button("Abrir", () => ConfirmDiscard(() => Pick(PickOpen))), Button("Guardar como", SaveChoice), tools);
        status = Text("Abre un bundle, un .assets o la demostración del menú."); root.AddView(status);
        progress = new(this, null, global::Android.Resource.Attribute.ProgressBarStyleHorizontal) { Indeterminate = true, Visibility = ViewStates.Gone };
        root.AddView(progress);
        var cancel = new MaterialButton(this) { Text = "Cancelar operación", Visibility = ViewStates.Gone };
        cancel.Click += (_, _) => operation?.Cancel(); root.AddView(cancel); cancelButton = cancel;
        content = new(this) { Orientation = Orientation.Vertical };
        root.AddView(content, new LinearLayout.LayoutParams(-1, 0, 1));
        SetContentView(root); ShowExplorer();
    }
    MaterialButton? cancelButton;

    void ShowExplorer()
    {
        content.RemoveAllViews();
        Row(content, Button("Objetos", () => { page = "Objetos"; ShowExplorer(); }),
            Button("Archivos", () => { page = "Archivos"; ShowExplorer(); }),
            Button("Cambios", ShowChanges), Button("Registro", ShowLogs));
        search = new(this) { Hint = "Buscar nombre, tipo, PathID o Container" }; search.SetSingleLine(true);
        content.AddView(search); search.TextChanged += (_, _) => RefreshList();
        filter = new(this);
        filter.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerDropDownItem,
            ["Todos los tipos", "Material", "Shader", "ShaderVariantCollection", "Texture2D", "TextAsset", "MonoBehaviour"]);
        content.AddView(filter); filter.ItemSelected += (_, _) => RefreshList();
        if (page == "Archivos") filter.Visibility = ViewStates.Gone;
        Row(content, Button("Seleccionar visibles", () =>
        {
            if (page == "Objetos") foreach (var item in visible) selected.Add(item.Key);
            else foreach (var entry in entries) selected.Add("entry:" + entry.Name);
            RefreshChecks();
        }), Button("Limpiar selección", () => { selected.Clear(); RefreshChecks(); }), Button("Exportar ZIP", ExportBatch));
        content.AddView(Text(page + " · toca para seleccionar, mantén pulsado para abrir", 12));
        list = new(this) { ChoiceMode = ChoiceMode.Multiple };
        list.ItemClick += (_, e) =>
        {
            string key = page == "Objetos" ? visible[e.Position].Key : "entry:" + entries[e.Position].Name;
            if (!selected.Add(key)) selected.Remove(key);
        };
        list.ItemLongClick += (_, e) =>
        {
            e.Handled = true; if (busy) return;
            if (page == "Objetos") ShowObject(visible[e.Position]); else ShowEntry(entries[e.Position]);
        };
        content.AddView(list, new LinearLayout.LayoutParams(-1, 0, 1)); RefreshList();
    }
    void RefreshList()
    {
        if (list == null) return;
        string query = search.Text?.Trim() ?? "";
        if (page == "Objetos")
        {
            int type = classes[Math.Clamp(filter.SelectedItemPosition, 0, classes.Length - 1)];
            visible = objects.Where(x => (type == 0 || x.ClassId == type) &&
                $"{x.Name} {x.Type} {x.PathId} {x.Container} {x.File}".Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            list.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleListItemMultipleChoice,
                visible.Select(x => $"{(x.Modified ? "● " : "")}{x.Name}\n{x.Type} · PathID {x.PathId} · ClassID {x.ClassId} · {x.Size:N0} B\n{x.Container}").ToArray());
        }
        else
        {
            entries = (document?.Entries ?? []).Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            list.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleListItemMultipleChoice,
                entries.Select(x => $"{(x.Modified ? "● " : "")}{x.Name}\n{(x.Serialized ? "SerializedFile" : "Recurso")} · {x.Size:N0} B").ToArray());
        }
        RefreshChecks();
    }
    void RefreshChecks()
    {
        for (int i = 0; i < (page == "Objetos" ? visible.Count : entries.Count); i++)
            list.SetItemChecked(i, selected.Contains(page == "Objetos" ? visible[i].Key : "entry:" + entries[i].Name));
    }

    async Task Run(Func<CancellationToken, Task> action)
    {
        if (busy) return;
        busy = true; operation = new(); progress.Visibility = ViewStates.Visible; cancelButton!.Visibility = ViewStates.Visible;
        Window!.AddFlags(WindowManagerFlags.KeepScreenOn);
        try { await action(operation.Token); }
        catch (OperationCanceledException) { status.Text = "Operación cancelada. El original permanece en la copia privada."; }
        catch (Exception e) { log.Add(e.ToString()); global::Android.Util.Log.Error("Uabea", e.ToString()); status.Text = "Error: " + e.Message; Message("Operación no completada", e.Message); }
        finally { busy = false; operation.Dispose(); operation = null; progress.Visibility = ViewStates.Gone; cancelButton.Visibility = ViewStates.Gone; Window.ClearFlags(WindowManagerFlags.KeepScreenOn); }
    }
    IProgress<string> ProgressText() => new Progress<string>(message => status.Text = message);
    async Task LoadPath(string path, Uri? uri, string name, CancellationToken cancel)
    {
        status.Text = "Leyendo el contenido completo…";
        var next = await Task.Run(() => UnityDocument.Open(path, work, db, cancel), cancel);
        IReadOnlyList<UnityObject> nextObjects;
        try { nextObjects = await Task.Run(() => next.ListObjects(cancel), cancel); }
        catch { next.Dispose(); throw; }
        document?.Dispose(); document = next; objects = nextObjects; sourceUri = uri; displayName = name;
        selected.Clear(); lastSaved = null;
        summary.Text = $"{name} · {objects.Count:N0} objetos";
        status.Text = "Abierto. El archivo original se conserva en el espacio privado.";
        page = "Objetos"; ShowExplorer();
    }
    async Task ReloadObjects(CancellationToken cancel)
    {
        objects = await Task.Run(() => document!.ListObjects(cancel), cancel);
        summary.Text = $"{displayName} · {objects.Count:N0} objetos · {document!.Changes.Count} cambios";
    }
    void ConfirmDiscard(Action next)
    {
        if (document?.Changes.Count > 0) Confirm("Abrir otro archivo", "Se descartarán los cambios sin exportar. La copia privada original se conserva.", next);
        else next();
    }
    void Pick(int code, string type = "*/*")
    {
        var intent = new Intent(Intent.ActionOpenDocument); intent.SetType(type); intent.AddCategory(Intent.CategoryOpenable);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission); StartActivityForResult(intent, code);
    }
    void ExportPicker(string path, string name, string mime = "application/octet-stream")
    {
        pendingExport = path;
        var intent = new Intent(Intent.ActionCreateDocument); intent.SetType(mime); intent.AddCategory(Intent.CategoryOpenable);
        intent.PutExtra(Intent.ExtraTitle, StreamTools.SafeName(name)); StartActivityForResult(intent, PickSave);
    }
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (resultCode != Result.Ok || data?.Data == null) return;
        Uri uri = data.Data;
        _ = Run(async token =>
        {
            if (requestCode == PickSave)
            {
                if (pendingExport == null) return;
                string path = pendingExport;
                await Task.Run(() => saf.ExportAsync(path, uri, sourceUri, token), token);
                status.Text = "Exportado y releído: SHA-256 coincide."; return;
            }
            try { ContentResolver!.TakePersistableUriPermission(uri, data.Flags & ActivityFlags.GrantReadUriPermission); } catch (Java.Lang.SecurityException) { }
            var copyProgress = new Progress<long>(bytes => status.Text = $"Copiando a espacio privado: {bytes / 1048576.0:F1} MiB");
            string local = await Task.Run(() => saf.ImportAsync(uri, token, copyProgress), token);
            if (requestCode == PickOpen) await LoadPath(local, uri, saf.DisplayName(uri), token);
            else if (requestCode == PickResource) { document!.AttachResource(saf.DisplayName(uri), local); status.Text = "Recurso asociado: " + saf.DisplayName(uri); }
            else if (requestCode == PickImport && importAction != null) { var action = importAction; importAction = null; await action(local, token); }
        });
    }

    void SaveChoice()
    {
        if (document == null) return;
        if (!document.IsBundle) { Save(BundleCompression.None); return; }
        new AlertDialog.Builder(this).SetTitle("Compresión de salida")!.SetItems(["LZ4 · recomendado", "Sin compresión", "LZMA · más lento"], (_, e) =>
            Save(e.Which switch { 1 => BundleCompression.None, 2 => BundleCompression.Lzma, _ => BundleCompression.Lz4 }))!.Show();
    }
    void Save(BundleCompression compression) => _ = Run(async token =>
    {
        string path = Path.Combine(work, "edited-" + Guid.NewGuid().ToString("N") + (document!.IsBundle ? ".bundle" : ".assets"));
        var reporter = ProgressText();
        var result = await Task.Run(() => document.SaveAs(path, compression, token, reporter), token);
        lastSaved = path; status.Text = $"Validado: {result.ObjectsVerified} objetos. Elige destino.";
        ExportPicker(path, "edited-" + displayName);
    });

    void ShowObject(UnityObject obj)
    {
        content.RemoveAllViews();
        content.AddView(Button("← Explorador", ShowExplorer));
        content.AddView(Text(obj.Name, 21));
        content.AddView(Text($"{obj.Type} · ClassID {obj.ClassId} · PathID {obj.PathId}\n{obj.Size:N0} bytes · Unity {obj.UnityVersion}\n{obj.File}\n{obj.Container}", 12));
        Row(content, Button("Propiedades", () => ShowProperties(obj)), Button("Hex / comparar", () => ShowHex(obj, 0)),
            Button("Exportar RAW", () => ExportObject(obj)), Button("Importar RAW", () =>
                Confirm("Reemplazar RAW", "Debe ser el objeto serializado completo y compatible. Sin esquema, solo se puede validar su integridad binaria.", () => Import(obj, "raw"))),
            Button("Revertir", () => _ = Run(async token => { document!.Revert(obj); await ReloadObjects(token); ShowExplorer(); })));
        if (obj.ClassId == 49) Row(content, Button("Editar texto", () => EditTextAsset(obj)), Button("Exportar texto", () => ExportObject(obj, "text")), Button("Importar texto", () => Import(obj, "text")));
        if (obj.ClassId == 28)
        {
            Row(content, Button("Vista previa", () => Preview(obj)), Button("Exportar PNG", () => ExportObject(obj, "png")),
                Button("Importar PNG", () => Confirm("Convertir a RGBA32", "Conservará dimensiones y cantidad de mipmaps. Puede aumentar el tamaño; no se recompone ASTC/ETC. Los mipmaps usan promedio de bytes.", () => Import(obj, "png"))));
            content.AddView(Text("La vista previa requiere el recurso .resS si la imagen está almacenada externamente."));
        }
        if (obj.ClassId == 21) content.AddView(Text("Material: edita m_SavedProperties para números, colores y vectores; m_Shader y m_Texture son referencias PPtr (FileID/PathID). No se cambian por un nombre arbitrario."));
        if (obj.ClassId == 48) content.AddView(Text("Shader: inspección de estructura y exportación RAW. El código GPU compilado no se convierte automáticamente a ShaderLab. Un reemplazo debe corresponder a plataforma y versión compatibles."));
        if (obj.ClassId == 114) content.AddView(Text("MonoBehaviour requiere TypeTree propio. No se infiere un esquema de scripts ausente."));
    }

    void ShowProperties(UnityObject obj) => _ = Run(async token =>
    {
        var rootField = await Task.Run(() => document!.ReadField(obj), token);
        string json = await Task.Run(() => PropertyEditor.ExportJson(rootField), token);
        if (json.Length > 1_000_000) { Message("Propiedades extensas", "El editor admite 1 MB de JSON. Usa RAW para este objeto."); return; }
        var edit = new EditText(this) { Text = json, TextSize = 12, Gravity = GravityFlags.Top, InputType = global::Android.Text.InputTypes.ClassText | global::Android.Text.InputTypes.TextFlagMultiLine };
        edit.SetTypeface(Typeface.Monospace, TypefaceStyle.Normal);
        var scroll = new ScrollView(this); scroll.AddView(edit);
        new AlertDialog.Builder(this).SetTitle("Propiedades · edita Value")!.SetMessage("Escalares serializados (máximo 10 000). Mantén Path y Type. Arrays: se editan elementos existentes.")!
            .SetView(scroll)!.SetNegativeButton("Cerrar", (_, _) => { })!.SetNeutralButton("Exportar JSON", (_, _) => _ = Run(async t =>
            {
                string path = Path.Combine(work, Guid.NewGuid() + ".json"); await File.WriteAllTextAsync(path, json, t); ExportPicker(path, obj.Name + ".json", "application/json");
            }))!.SetPositiveButton("Aplicar", (_, _) =>
            {
                string edited = edit.Text ?? "[]";
                _ = Run(async t =>
                {
                    await Task.Run(() => { var f = document!.ReadEditableField(obj); PropertyEditor.ApplyJson(f, edited); document.ReplaceField(obj, f, "Propiedades serializadas"); }, t);
                    await ReloadObjects(t); status.Text = "Modificación preparada. Usa Guardar como."; ShowExplorer();
                });
            })!.Show();
    });

    void EditTextAsset(UnityObject obj) => _ = Run(async token =>
    {
        byte[] bytes = await Task.Run(() => TextAssetCodec.Export(document!, obj), token);
        if (bytes.Length > 1_000_000) throw new IOException("Texto mayor de 1 MB: usa exportación e importación.");
        string text = new UTF8Encoding(false, true).GetString(bytes);
        var edit = new EditText(this) { Text = text, Gravity = GravityFlags.Top, InputType = global::Android.Text.InputTypes.ClassText | global::Android.Text.InputTypes.TextFlagMultiLine };
        new AlertDialog.Builder(this).SetTitle("TextAsset · UTF-8")!.SetView(edit)!.SetNegativeButton("Cancelar", (_, _) => { })!
            .SetPositiveButton("Aplicar", (_, _) =>
            {
                string value = edit.Text ?? "";
                _ = Run(async t => { await Task.Run(() => TextAssetCodec.Import(document!, obj, Encoding.UTF8.GetBytes(value)), t); await ReloadObjects(t); ShowExplorer(); });
            })!.Show();
    });
    void Import(UnityObject obj, string kind)
    {
        importAction = async (path, token) =>
        {
            await Task.Run(() =>
            {
                if (new FileInfo(path).Length > document!.Limits.MaxObjectBytes) throw new IOException("Importación mayor del límite de objeto.");
                byte[] bytes = File.ReadAllBytes(path);
                if (kind == "text") TextAssetCodec.Import(document!, obj, bytes);
                else if (kind == "png") TextureCodec.ImportPngRgba32(document!, obj, bytes);
                else document!.ReplaceRaw(obj, bytes);
            }, token);
            await ReloadObjects(token); ShowExplorer(); status.Text = "Reemplazo preparado; usa Guardar como.";
        };
        Pick(PickImport, kind == "png" ? "image/png" : "*/*");
    }
    void ExportObject(UnityObject obj, string kind = "raw") => _ = Run(async token =>
    {
        string extension = kind == "png" ? ".png" : kind == "text" ? ".txt" : ".raw";
        string path = Path.Combine(work, Guid.NewGuid() + extension);
        await Task.Run(() =>
        {
            using var output = File.Create(path);
            if (kind == "png") output.Write(TextureCodec.ExportPng(document!, obj));
            else if (kind == "text") output.Write(TextAssetCodec.Export(document!, obj));
            else { using var raw = document!.OpenRaw(obj); StreamTools.Copy(raw, output, raw.Length, token); }
        }, token);
        ExportPicker(path, obj.Name + "-" + obj.PathId + extension, kind == "png" ? "image/png" : "application/octet-stream");
    });
    void Preview(UnityObject obj) => _ = Run(async token =>
    {
        var info = await Task.Run(() => TextureCodec.Info(document!, obj), token);
        var png = await Task.Run(() => TextureCodec.ExportPng(document!, obj), token);
        var bitmap = await BitmapFactory.DecodeByteArrayAsync(png, 0, png.Length) ?? throw new IOException("PNG inválido.");
        var image = new ImageView(this); image.SetImageBitmap(bitmap); image.SetAdjustViewBounds(true);
        var dialog = new AlertDialog.Builder(this).SetTitle($"{info.Width} × {info.Height} · {info.Format} · {info.MipCount} mip(s)")!.SetView(image)!.SetPositiveButton("Cerrar", (_, _) => { })!.Create();
        dialog.DismissEvent += (_, _) => { image.SetImageDrawable(null); bitmap.Dispose(); }; dialog.Show();
    });

    void ShowHex(UnityObject obj, long offset) => _ = Run(async token =>
    {
        var result = await Task.Run(() =>
        {
            using var current = document!.OpenRaw(obj); using var original = document.OpenRaw(obj, true);
            long start = Math.Clamp(offset, 0, Math.Max(0, current.Length - 1)); current.Position = start;
            byte[] data = new byte[(int)Math.Min(512, current.Length - start)]; current.ReadExactly(data);
            byte[] previous = new byte[(int)Math.Max(0, Math.Min(data.Length, original.Length - start))];
            if (previous.Length > 0) { original.Position = start; original.ReadExactly(previous); }
            var text = new StringBuilder();
            for (int i = 0; i < data.Length; i += 8)
            {
                text.Append($"{start + i:X8}  ");
                for (int j = i; j < Math.Min(i + 8, data.Length); j++) text.Append($"{data[j]:X2}{(j >= previous.Length || previous[j] != data[j] ? '*' : ' ')}");
                text.AppendLine();
            }
            return (Text: text.ToString(), Length: current.Length);
        }, token);
        content.RemoveAllViews(); content.AddView(Button("← Objeto", () => ShowObject(obj)));
        content.AddView(Text($"Hex · {offset:X} / {result.Length:X} · * = cambio respecto al original", 12));
        Row(content, Button("Anterior", () => ShowHex(obj, Math.Max(0, offset - 512))),
            Button("Siguiente", () => ShowHex(obj, Math.Min(Math.Max(0, result.Length - 1), offset + 512))),
            Button("Ir a offset", () => Prompt("Offset hexadecimal", "0", value => { if (long.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var n)) ShowHex(obj, n); })),
            Button("Cambiar bytes", () => Prompt("OFFSET_HEX: AA BB CC", "0: ", value => _ = Run(async t =>
            {
                await Task.Run(() =>
                {
                    var parts = value.Split(':', 2); int pos = Convert.ToInt32(parts[0].Trim(), 16);
                    byte[] patch = Convert.FromHexString(parts[1].Replace(" ", "")); byte[] bytes = document!.ReadRaw(obj);
                    if (pos < 0 || pos > bytes.Length - patch.Length) throw new FormatException("Rango fuera del objeto.");
                    patch.CopyTo(bytes, pos); document.ReplaceRaw(obj, bytes, "Parche hexadecimal");
                }, t);
                await ReloadObjects(t); ShowExplorer();
            }))));
        var view = Text(result.Text, 12); view.SetTypeface(Typeface.Monospace, TypefaceStyle.Normal); view.SetTextIsSelectable(true);
        var scroll = new ScrollView(this); scroll.AddView(view); content.AddView(scroll, new LinearLayout.LayoutParams(-1, 0, 1));
    });

    void ShowEntry(BundleEntry entry)
    {
        new AlertDialog.Builder(this).SetTitle(entry.Name)!.SetItems(["Extraer entrada", "Reemplazar entrada completa"], (_, e) =>
        {
            if (e.Which == 0) _ = Run(async token =>
            {
                string path = Path.Combine(work, Guid.NewGuid() + ".entry");
                await Task.Run(() => { using var output = File.Create(path); document!.ExtractEntry(entry.Name, output, token); }, token);
                ExportPicker(path, entry.Name);
            });
            else Confirm("Reemplazo de entrada", "Los datos seleccionados reemplazarán esta entrada al guardar una copia nueva.", () =>
            {
                importAction = async (path, token) => { await Task.Run(() => document!.ReplaceEntry(entry.Name, path, token), token); await ReloadObjects(token); ShowExplorer(); };
                Pick(PickImport);
            });
        })!.Show();
    }
    void ExportBatch() => _ = Run(async token =>
    {
        if (document == null || selected.Count == 0) return;
        var selection = selected.ToHashSet();
        string path = Path.Combine(work, Guid.NewGuid() + ".zip");
        await Task.Run(() =>
        {
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var obj in objects.Where(x => selection.Contains(x.Key)))
            {
                token.ThrowIfCancellationRequested();
                string name = StreamTools.SafeName(obj.File + "-" + obj.PathId + "-" + obj.Name) + ".raw";
                using var output = archive.CreateEntry("objects/" + name, CompressionLevel.NoCompression).Open();
                using var data = document.OpenRaw(obj); StreamTools.Copy(data, output, data.Length, token);
            }
            int index = 0;
            foreach (var entry in document.Entries.Where(x => selection.Contains("entry:" + x.Name)))
            {
                using var output = archive.CreateEntry($"entries/{index++}-" + StreamTools.SafeName(entry.Name), CompressionLevel.NoCompression).Open();
                document.ExtractEntry(entry.Name, output, token);
            }
        }, token);
        ExportPicker(path, "UABEA-export.zip", "application/zip");
    });

    void ShowChanges()
    {
        content.RemoveAllViews(); content.AddView(Button("← Explorador", ShowExplorer));
        var text = new StringBuilder("Modificaciones pendientes\n\n");
        foreach (var change in document?.Changes ?? []) text.AppendLine($"{change.Key}\n{change.Description}\n{change.BeforeSize:N0} → {change.AfterSize:N0} bytes\nSHA-256 antes: {change.BeforeSha256}\nDespués: {change.AfterSha256}\n");
        text.Append("Para deshacer un objeto: ábrelo y toca Revertir. Para deshacer una entrada completa: vuelve a abrir el original.");
        var scroll = new ScrollView(this); var label = Text(text.ToString(), 12); label.SetTextIsSelectable(true); scroll.AddView(label); content.AddView(scroll);
    }
    void ShowLogs()
    {
        content.RemoveAllViews(); content.AddView(Button("← Explorador", ShowExplorer));
        var scroll = new ScrollView(this); var label = Text(string.Join("\n\n", (document?.Logs ?? []).Concat(log)), 12); label.SetTextIsSelectable(true); scroll.AddView(label); content.AddView(scroll);
    }
    void ShowTools(View anchor)
    {
        var menu = new PopupMenu(this, anchor);
        string[] items = ["Abrir demostración", "Añadir recurso .resS", "Abrir con root", "Exportar última copia con root", "Autoprueba Android / SAF", "Cambiar tema", "Licencias y límites", "Almacenamiento y permisos", "Comprobar root"];
        for (int i = 0; i < items.Length; i++) menu.Menu!.Add(0, i, i, items[i]);
        menu.MenuItemClick += (_, e) =>
        {
            switch (e.Item!.ItemId)
            {
                case 0: ConfirmDiscard(() => _ = Run(async token => { string path = await Task.Run(() => FixtureFactory.Create(Path.Combine(work, "demo"), db), token); await LoadPath(path, null, "fixture.bundle", token); })); break;
                case 1: if (document != null) Pick(PickResource); break;
                case 2: ConfirmDiscard(() => Prompt("Ruta absoluta para lectura root", "/sdcard/Download/archivo.bundle", path => _ = Run(async token =>
                {
                    if (!await RootStorage.RequestAccess(token)) throw new IOException("No se concedió root.");
                    string copy = await Task.Run(() => RootStorage.Import(path, work, token), token); await LoadPath(copy, null, Path.GetFileName(path), token);
                }))); break;
                case 3:
                    if (lastSaved == null) { Message("Primero guarda", "Usa Guardar como para generar y validar una copia."); break; }
                    Prompt("Ruta de destino root", "/sdcard/Download/edited.bundle", path => Confirm("Confirmar escritura protegida", $"Destino exacto:\n{path}\n\nSi existe, se creará una copia .uabea-backup antes de sustituirlo.", () => _ = Run(async token =>
                    { string backup = await Task.Run(() => RootStorage.ExportConfirmed(lastSaved!, path, token), token); status.Text = backup.Length == 0 ? "Exportación root verificada. Se creó un archivo nuevo." : "Exportación root verificada. Copia previa: " + backup; log.Add(status.Text); }))); break;
                case 4: _ = Run(async token => { string report = await AndroidSelfTest.Run(this, work, db, token); log.Add(report); Message("Autoprueba", report); }); break;
                case 5:
                    AppCompatDelegate.DefaultNightMode = AppCompatDelegate.DefaultNightMode == AppCompatDelegate.ModeNightYes ? AppCompatDelegate.ModeNightNo : AppCompatDelegate.ModeNightYes;
                    break;
                case 6: _ = Run(async token =>
                {
                    var text = new StringBuilder("UABEA Android · port experimental con AssetsTools.NET.\nNo afiliado con Unity/nesrak1.\nLímites: archivo 2 GiB; expansión 4 GiB; edición por objeto 64 MiB; texturas 4 MP. MonoBehaviour necesita TypeTree. Importación PNG convierte a RGBA32. Sin cifrado, anticheat ni compilador ShaderLab.\n\n");
                    foreach (string name in Assets!.List("licenses") ?? []) { using var reader = new StreamReader(Assets.Open("licenses/" + name)); text.AppendLine(await reader.ReadToEndAsync(token)); }
                    Message("Licencias y limitaciones", text.ToString());
                }); break;
                case 7: _ = Run(async token =>
                {
                    string report = await Task.Run(() => StorageDiagnostics.Check(this, work, sourceUri, token), token);
                    log.Add(report); Message("Almacenamiento y permisos", report);
                }); break;
                case 8: _ = Run(async token =>
                {
                    status.Text = "Solicitando root a KernelSU/SukiSU…";
                    string report = await Task.Run(() => RootStorage.Diagnose(token), token);
                    log.Add(report); Message("Diagnóstico root", report);
                }); break;
            }
        };
        menu.Show();
    }
    void Message(string title, string text) => new AlertDialog.Builder(this).SetTitle(title)!.SetMessage(text)!.SetPositiveButton("Aceptar", (_, _) => { })!.Show();
    void Confirm(string title, string text, Action confirmed) => new AlertDialog.Builder(this).SetTitle(title)!.SetMessage(text)!.SetNegativeButton("Cancelar", (_, _) => { })!.SetPositiveButton("Continuar", (_, _) => confirmed())!.Show();
    void Prompt(string title, string value, Action<string> action)
    {
        var input = new EditText(this) { Text = value }; input.SetSingleLine(true);
        new AlertDialog.Builder(this).SetTitle(title)!.SetView(input)!.SetNegativeButton("Cancelar", (_, _) => { })!.SetPositiveButton("Aceptar", (_, _) => action(input.Text ?? ""))!.Show();
    }
}
