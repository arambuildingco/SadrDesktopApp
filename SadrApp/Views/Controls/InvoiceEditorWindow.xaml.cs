using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using SadrApp.Data;
using SadrApp.Infrastructure;

namespace SadrApp.Views.Controls;

/// <summary>
/// Editable invoice line row for the DataGrid. Implements INotifyPropertyChanged so
/// dependent cells (جمع ردیف) update immediately when any input column changes —
/// no Items.Refresh (which throws inside an AddNew/EditItem transaction).
/// </summary>
public class InvoiceLineRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        // Money columns depend on every other money column.
        if (name is not (nameof(TotalText) or nameof(DiscountRowText)))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DiscountRowText)));
        }
    }

    private int _index;
    private int _detailId;
    private int _productId;
    private string _productName = "";
    private int _unitId;
    private string _unitName = "";
    private decimal _amount = 1;
    private decimal? _unitPrice;
    private int? _currencyId;
    private string _currencyName = "";
    private int? _priceId;
    private double? _extraPercent;
    private double? _discountPercent;
    private decimal? _discountAmount;

    public int Index { get => _index; set => Set(ref _index, value); }
    /// <summary>Existing InvoiceDetail Id for loaded rows (0 on new rows) — drives line matching in SaveAsync,
    /// so the same product may appear twice in one invoice.</summary>
    public int DetailId { get => _detailId; set => Set(ref _detailId, value); }
    public int ProductId { get => _productId; set => Set(ref _productId, value); }
    public string ProductName { get => _productName; set => Set(ref _productName, value); }
    public int UnitId { get => _unitId; set => Set(ref _unitId, value); }
    public string UnitName { get => _unitName; set => Set(ref _unitName, value); }
    public decimal Amount { get => _amount; set => Set(ref _amount, value); }
    public decimal? UnitPrice { get => _unitPrice; set => Set(ref _unitPrice, value); }
    public int? CurrencyId { get => _currencyId; set => Set(ref _currencyId, value); }
    public string CurrencyName { get => _currencyName; set => Set(ref _currencyName, value); }
    /// <summary>Price row the unit price came from (drives later "from this price" reporting).</summary>
    public int? PriceId { get => _priceId; set => Set(ref _priceId, value); }
    public double? ExtraPercent { get => _extraPercent; set => Set(ref _extraPercent, value); }
    public double? DiscountPercent { get => _discountPercent; set => Set(ref _discountPercent, value); }
    public decimal? DiscountAmount { get => _discountAmount; set => Set(ref _discountAmount, value); }

    public decimal Total =>
        Math.Round((UnitPrice ?? 0m) * Amount
                   * (1 + (decimal)((ExtraPercent ?? 0) / 100.0))
                   * (1 - (decimal)((DiscountPercent ?? 0) / 100.0))
                   - (DiscountAmount ?? 0m), 0);

    public decimal DiscountRow =>
        Math.Round(((UnitPrice ?? 0m) * Amount * (decimal)((DiscountPercent ?? 0) / 100.0)) + (DiscountAmount ?? 0m), 0);

    // Setters use NumberInput parsing so thousands separators typed in the grid are accepted.
    // Display side uses grouped thousands (round-trips through the separator-tolerant setters).
    public string AmountText { get => Amount.ToString("#,##0.###", CultureInfo.InvariantCulture); set { var d = NumberInput.ParseDecimal(value); if (d is not null) Amount = d.Value; } }
    public string PriceText { get => UnitPrice?.ToString("#,##0.##", CultureInfo.InvariantCulture) ?? ""; set { var d = NumberInput.ParseDecimal(value); if (d is not null) UnitPrice = d.Value; } }
    public string ExtraText { get => ExtraPercent?.ToString("0.##") ?? ""; set { var d = NumberInput.ParseDouble(value); if (d is not null) ExtraPercent = d.Value; } }
    public string DiscountText { get => DiscountPercent?.ToString("0.##") ?? ""; set { var d = NumberInput.ParseDouble(value); if (d is not null) DiscountPercent = d.Value; } }
    public string DiscAmtText { get => DiscountAmount?.ToString("#,##0.##", CultureInfo.InvariantCulture) ?? ""; set { var d = NumberInput.ParseDecimal(value); if (d is not null) DiscountAmount = d.Value; } }
    public string TotalText => Total.ToString("N0");
    public string DiscountRowText => DiscountRow.ToString("N0");
}

public partial class InvoiceEditorWindow : Window
{
    private class PickedProduct
    {
        public int Id;
        public string Name = "";
        public int MainUnitId;
        public string UnitName = "";
        public decimal? Price;
        public int? CurrencyId;
        public string CurrencyName = "";
        public int? PriceId;
        /// <summary>Default row discount (٪) from the Price row.</summary>
        public double DiscountPercent;
    }

    private readonly ObservableCollection<InvoiceLineRow> _rows = new();
    private readonly List<PickedProduct> _catalog = new();
    /// <summary>Latest (most recently dated) Price row per product — the invoice default.</summary>
    private readonly Dictionary<int, (int PriceId, decimal Amount, int? CurrencyId, double DiscountPercent)> _latestPrice = new();
    private readonly int? _invoiceId;
    private readonly int _initialType;
    private bool _loadedOk;

    /// <summary>After OK: the saved invoice id (null when cancelled).</summary>
    public int? SavedInvoiceId { get; private set; }

    public InvoiceEditorWindow(int? invoiceId, int invoiceType)
    {
        InitializeComponent();
        _invoiceId = invoiceId;
        _initialType = invoiceType;
        BtnPrint.Visibility = invoiceId is int ? Visibility.Visible : Visibility.Hidden;
        BtnMoney.Visibility = invoiceId is int ? Visibility.Visible : Visibility.Hidden;

        CmbType.ItemsSource = new[]
        {
            new KeyValuePair<int, string>(InvoiceTypeConsts.Sell, InvoiceTypeConsts.Label(InvoiceTypeConsts.Sell)),
            new KeyValuePair<int, string>(InvoiceTypeConsts.Buy, InvoiceTypeConsts.Label(InvoiceTypeConsts.Buy)),
            new KeyValuePair<int, string>(InvoiceTypeConsts.PreInvoice, InvoiceTypeConsts.Label(InvoiceTypeConsts.PreInvoice)),
        };
        CmbType.DisplayMemberPath = "Value";
        CmbType.SelectedValuePath = "Key";
        CmbType.SelectedValue = invoiceType;
        CmbType.SelectionChanged += (_, _) => UpdatePartyLabel();

        CmbStatus.ItemsSource = new[]
        {
            new KeyValuePair<int, string>(InvoiceStatusConsts.Draft, InvoiceStatusConsts.Label(InvoiceStatusConsts.Draft)),
            new KeyValuePair<int, string>(InvoiceStatusConsts.Issued, InvoiceStatusConsts.Label(InvoiceStatusConsts.Issued)),
            new KeyValuePair<int, string>(InvoiceStatusConsts.Confirmed, InvoiceStatusConsts.Label(InvoiceStatusConsts.Confirmed)),
            new KeyValuePair<int, string>(InvoiceStatusConsts.Cancelled, InvoiceStatusConsts.Label(InvoiceStatusConsts.Cancelled)),
        };
        CmbStatus.DisplayMemberPath = "Value";
        CmbStatus.SelectedValuePath = "Key";
        CmbStatus.SelectedValue = InvoiceStatusConsts.Draft;

        Grid.ItemsSource = _rows;
        Grid.PreparingCellForEdit += Grid_PreparingCellForEdit;
        _rows.CollectionChanged += (_, _) => RecomputeTotals();
        BtnAddRow.Click += (_, _) => AddRow();
        BtnRemoveRow.Click += (_, _) => RemoveRow();
        BtnSave.Click += async (_, _) => await SaveAsync();
        BtnPrint.Click += async (_, _) => await PrintSavedInvoiceAsync();
        BtnMoney.Click += (_, _) =>
        {
            if (_invoiceId is int id)
                new InvoiceMoneyWindow(id, TxtNumber.Text).ShowDialog();
        };

        Loaded += async (_, _) =>
        {
            if (_loadedOk) return;
            _loadedOk = true;
            await LoadDataAsync();
        };
    }

    /// <summary>Shows the print preview for the invoice currently loaded in the editor.</summary>
    private async Task PrintSavedInvoiceAsync()
    {
        if (_invoiceId is not int id)
        {
            MessageBox.Show("ابتدا فاکتور را ذخیره کنید، سپس چاپ در دسترس است.", "اطلاع",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            IsEnabled = false;
            await InvoicePrintService.OpenPreviewAsync(id, Window.GetWindow(this));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در چاپ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async Task LoadDataAsync()
    {
        try
        {
            await using var db = SadrDb.New();

            var currencies = await db.Currencies.Where(c => !c.Deleted)
                .Select(c => new { c.Id, c.Name }).ToListAsync();
            var defaultCurrency = currencies.FirstOrDefault()?.Id;

            var units = await db.Units.Where(u => !u.Deleted)
                .Select(u => new { u.Id, u.Name }).ToListAsync();
            var unitNames = units.ToDictionary(u => u.Id, u => u.Name);
            var currencyNames = currencies.ToDictionary(c => c.Id, c => c.Name);

            // Product catalog with the latest defined price (most recent SetDate, then Id).
            // The old join picked an arbitrary row when several price rows existed.
            var priceRows = await db.Prices.Where(x => !x.Deleted).ToListAsync();
            var latestPerPair = priceRows
                .GroupBy(x => (x.ProductId, x.UnitId))
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.SetDateG).ThenByDescending(x => x.Id).First());
            var latestForProduct = priceRows
                .GroupBy(x => x.ProductId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.SetDateG).ThenByDescending(x => x.Id).First());

            var prodList = await db.Products.Where(p => !p.Deleted)
                .OrderBy(p => p.Name)
                .Select(p => new { p.Id, p.Name, p.MainUnitId })
                .ToListAsync();
            foreach (var p in prodList)
            {
                var chosen = latestPerPair.TryGetValue((p.Id, p.MainUnitId), out var mp) ? mp
                    : latestForProduct.TryGetValue(p.Id, out var ap) ? ap
                    : null;
                _catalog.Add(new PickedProduct
                {
                    Id = p.Id,
                    Name = p.Name,
                    MainUnitId = p.MainUnitId,
                    UnitName = unitNames.TryGetValue(p.MainUnitId, out var un) ? un : "",
                    Price = chosen?.PriceAmount,
                    CurrencyId = chosen?.CurrencyId,
                    CurrencyName = chosen is not null && currencyNames.TryGetValue(chosen.CurrencyId, out var cn) ? cn : "",
                    PriceId = chosen?.Id,
                    DiscountPercent = chosen?.DefaultDiscountPersentage ?? 0
                });
            }
            foreach (var (pid, pr) in latestForProduct)
                _latestPrice[pid] = (pr.Id, pr.PriceAmount, pr.CurrencyId, pr.DefaultDiscountPersentage);

            var parties = await db.CustomersProviders.Where(c => !c.Deleted)
                .OrderBy(c => c.Name)
                .Select(c => new { c.Id, c.Name }).ToListAsync();
            var partyItems = parties.Select(p => new ComboItem { Key = p.Id, Text = p.Name }).ToList();
            partyItems.Insert(0, new ComboItem { Key = 0, Text = "— انتخاب نشده —" });
            CmbParty.ItemsSource = partyItems;
            CmbParty.DisplayMemberPath = "Text";
            CmbParty.SelectedValuePath = "Key";

            if (_invoiceId is int id)
            {
                var inv = await db.Invoices.Include(i => i.Details).FirstAsync(i => i.Id == id);
                Title = "ویرایش فاکتور";
                HeaderTitle.Text = "ویرایش فاکتور";
                DpDate.Value = inv.InvoiceDateG;
                TxtNumber.Text = inv.InvoiceNumber;
                TxtCode.Text = inv.Code ?? "";
                TxtDescription.Text = inv.Description ?? "";
                CmbType.SelectedValue = inv.InvoiceType;
                CmbStatus.SelectedValue = inv.Status;
                CmbParty.SelectedValue = inv.InvoiceType == InvoiceTypeConsts.Buy ? inv.ProviderId ?? 0 : inv.CustomerId ?? 0;

                foreach (var d in inv.Details.Where(x => !x.Deleted).OrderBy(x => x.Id))
                {
                    var prod = _catalog.FirstOrDefault(c => c.Id == d.ProductId);
                    _rows.Add(new InvoiceLineRow
                    {
                        DetailId = d.Id,
                        ProductId = d.ProductId,
                        ProductName = prod?.Name ?? ("کالای #" + d.ProductId),
                        UnitId = d.UnitId,
                        UnitName = unitNames.TryGetValue(d.UnitId, out var un) ? un : "",
                        Amount = d.Amount,
                        UnitPrice = d.UnitPriceAmount,
                        CurrencyId = d.CurrencyId,
                        CurrencyName = d.CurrencyId is int dc && currencyNames.TryGetValue(dc, out var dn) ? dn : "",
                        PriceId = d.PriceId,
                        ExtraPercent = d.ExtraPersentage,
                        DiscountPercent = d.DiscountPersentage,
                        DiscountAmount = d.TotalDiscount
                    });
                }
            }
            else
            {
                DpDate.Value = DateTime.Now;
                TxtNumber.Text = await NextNumber(db, _initialType);
                AddPlaceholderRow();
            }
            UpdatePartyLabel();
            RecomputeTotals();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در بارگذاری فاکتور", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddPlaceholderRow()
    {
        if (_rows.Count > 0) return;
        // New invoice starts with an EMPTY row — no default product is imposed.
        // The seller picks one by double-clicking the کالا column.
        _rows.Add(new InvoiceLineRow { Amount = 1 });
        Renumber();
    }

    private void AddRow()
    {
        // Reuse the first empty row (the placeholder or a cancelled pick) instead of adding row 2.
        var target = _rows.FirstOrDefault(r => r.ProductId == 0);
        bool appended = false;
        if (target is null)
        {
            target = new InvoiceLineRow { Amount = 1 };
            _rows.Add(target);
            appended = true;
        }
        PickProductIntoRow(target);
        if (target.ProductId == 0 && appended)
        {
            // picker cancelled on a fresh row — don't leave a stray empty row behind
            _rows.Remove(target);
            Renumber();
        }
        RecomputeTotals();
    }

    /// <summary>Opens the product picker and fills the row with the chosen product.</summary>
    private void PickProductIntoRow(InvoiceLineRow row)
    {
        var picked = PickProduct();
        if (picked is null) return;
        row.ProductId = picked.Id;
        row.ProductName = picked.Name;
        row.UnitId = picked.MainUnitId;
        row.UnitName = picked.UnitName;
        row.UnitPrice = picked.Price;
        row.CurrencyId = picked.CurrencyId;
        row.CurrencyName = picked.CurrencyName;
        row.PriceId = picked.PriceId;
        // Default discount from the product's Price row (۰ = no default).
        row.DiscountPercent = picked.DiscountPercent > 0 ? picked.DiscountPercent : null;
        RecomputeTotals();
    }

    private PickedProduct? PickProduct()
    {
        var dlg = new Window
        {
            Title = "انتخاب کالا",
            Width = 460,
            Height = 520,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FlowDirection = FlowDirection.RightToLeft,
            Background = System.Windows.Media.Brushes.White
        };
        var panel = new StackPanel { Margin = new Thickness(10) };
        var search = new TextBox { Style = (Style)FindResource("Input"), ToolTip = "جستجوی کالا" };
        var list = new ListBox { Height = 380, Margin = new Thickness(0, 8, 0, 0), FontSize = 13 };
        var btnNew = new Button
        {
            Content = "➕ کالای جدید",
            Style = (Style)FindResource("PrimaryBtn"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
            ToolTip = "تعریف کالای جدید بدون خروج از فرم فاکتور"
        };
        panel.Children.Add(search);
        panel.Children.Add(list);
        panel.Children.Add(btnNew);
        dlg.Content = panel;

        void Fill(string q)
        {
            list.ItemsSource = _catalog
                .Where(c => q.Length == 0 || c.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Select(c => new { c.Id, Display = c.Name + "  (" + c.UnitName + ")" })
                .ToList();
            list.DisplayMemberPath = "Display";
            list.SelectedValuePath = "Id";
        }
        Fill("");
        search.TextChanged += (_, _) => Fill(search.Text.Trim());

        PickedProduct? result = null;
        btnNew.Click += (_, _) =>
        {
            var created = CreateProductInline(dlg);
            if (created is not null)
            {
                result = created;
                dlg.Close();
            }
        };
        list.MouseDoubleClick += (_, _) =>
        {
            if (list.SelectedValue is int id)
            {
                result = _catalog.FirstOrDefault(c => c.Id == id);
                dlg.Close();
            }
        };
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && list.SelectedValue is int id2)
            {
                result = _catalog.FirstOrDefault(c => c.Id == id2);
                dlg.Close();
            }
        };
        dlg.ShowDialog();
        return result;
    }

    /// <summary>Creates a product without leaving the invoice (used by the picker's «کالای جدید» button).
    /// On success the new product is added to the catalog and returned, ready to fill a row.</summary>
    private PickedProduct? CreateProductInline(Window dialogOwner)
    {
        try
        {
            using var db = SadrDb.New();
            var catChoices = db.ProductCategories.Where(c => !c.Deleted)
                .Select(c => new KeyValuePair<int, string>(c.Id, c.Name)).ToList();
            var unitChoices = db.Units.Where(u => !u.Deleted)
                .Select(u => new KeyValuePair<int, string>(u.Id, u.Name)).ToList();
            var brandChoices = db.Brands.Where(b => !b.Deleted)
                .Select(b => new KeyValuePair<int, string>(b.Id, b.Name)).ToList();

            var ed = new FieldEditorWindow("کالای جدید", new[]
            {
                FieldSpec.Text_("نام کالا", null, true),
                FieldSpec.Text_("کد کالا", null, true),
                FieldSpec.Multi_("توضیحات"),
                FieldSpec.Choice_("دسته اصلی", catChoices, null),
                FieldSpec.Choice_("واحد اصلی", unitChoices, null),
                FieldSpec.Choice_("برند", brandChoices, null, optional: true),
                FieldSpec.Numeric_("موجودی اولیه"),
            }) { Owner = dialogOwner };
            if (ed.ShowDialog() != true) return null;

            var name = ed.GetText(0)!.Trim();
            var code = ed.GetText(1)!.Trim();
            if (name.Length == 0 || code.Length == 0)
            {
                MessageBox.Show("نام و کد کالا الزامی است.", "ذخیره ممکن نیست",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            if (db.Products.Any(x => !x.Deleted && x.Code == code))
            {
                MessageBox.Show(CodeRules.MsgCodeDuplicate, "ذخیره ممکن نیست",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            var now = DateTime.Now;
            var p = new Product
            {
                RecordUniqueId = Guid.NewGuid(),
                CreateDateTime = now,
                Name = name,
                Code = code,
                Description = ed.GetText(2) ?? "",
                MainCategoryId = ed.GetChoice(3) ?? 0,
                MainUnitId = ed.GetChoice(4) ?? 0,
                BrandId = ed.GetChoice(5),
                InitialQuantity = ed.GetNumber(6) ?? 0,
                UpdateDateTime = now
            };
            db.Products.Add(p);
            db.SaveChanges();

            var picked = new PickedProduct
            {
                Id = p.Id,
                Name = p.Name,
                MainUnitId = p.MainUnitId,
                UnitName = db.Units.Where(u => u.Id == p.MainUnitId).Select(u => u.Name).FirstOrDefault() ?? ""
                // no price rows yet — price/currency stay empty until defined
            };
            _catalog.Add(picked);
            return picked;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در ذخیره کالا", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }

    private void RemoveRow()
    {
        if (Grid.SelectedItem is InvoiceLineRow row)
        {
            _rows.Remove(row);
            Renumber();
            RecomputeTotals();
        }
        else
        {
            ErrorText.Text = "ابتدا یک ردیف را انتخاب کنید.";
        }
    }

    private void Renumber()
    {
        for (int i = 0; i < _rows.Count; i++) _rows[i].Index = i + 1;
    }

    private static readonly string[] NumericColumnHeaders = { "مقدار", "قیمت واحد", "اضافه ٪", "تخفیف ٪", "تخفیف مبلغ" };

    private void Grid_PreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.Column is DataGridTextColumn col && NumericColumnHeaders.Contains(col.Header.ToString())
            && e.EditingElement is TextBox tb)
        {
            NumberInput.Attach(tb);
        }
    }

    private void Grid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is InvoiceLineRow row && e.Column is DataGridTextColumn col)
        {
            var header = col.Header.ToString();
            if (header == "کالا")
            {
                e.Cancel = true;
                PickProductIntoRow(row); // product chosen via the picker dialog
                return;
            }
            if (header == "واحد" || header == "ارز")
                e.Cancel = true; // these come from the product picker
        }
    }

    private void Grid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        // Row cells update themselves via INPC; this only refreshes the total bars after commit.
        if (e.EditAction == DataGridEditAction.Commit)
            Dispatcher.BeginInvoke(RecomputeTotals);
    }

    /// <summary>Recomputes the total bars. Row cells are INPC-driven and update immediately.</summary>
    private void RecomputeTotals()
    {
        TxtTotal.Text = "جمع کل: " + _rows.Sum(r => r.Total).ToString("N0");
        TxtTotalDiscount.Text = "تخفیف کل: " + _rows.Sum(r => r.DiscountRow).ToString("N0");
    }

    private void UpdatePartyLabel()
    {
        var t = CmbType.SelectedValue as int? ?? InvoiceTypeConsts.Sell;
        LblParty.Text = t switch
        {
            InvoiceTypeConsts.Buy => "تأمین‌کننده",
            InvoiceTypeConsts.PreInvoice => "مشتری",
            _ => "مشتری"
        };
    }

    private static async Task<string> NextNumber(SadrDbContext db, int type)
    {
        var y = PersianDate.Today[..4];
        var prefix = type switch
        {
            InvoiceTypeConsts.Buy => "B-",
            InvoiceTypeConsts.PreInvoice => "P-",
            _ => "S-"
        };
        var count = await db.Invoices.CountAsync(i => i.InvoiceType == type) + 1;
        return $"{prefix}{y}-{count:0000}";
    }

    private async Task SaveAsync()
    {
        ErrorText.Text = "";
        try
        {
            var rows = _rows.Where(r => r.ProductId > 0).ToList();
            if (rows.Count == 0)
            {
                ErrorText.Text = "حداقل یک ردیف کالا لازم است.";
                return;
            }
            if (DpDate.Value is null)
            {
                ErrorText.Text = "تاریخ فاکتور را وارد کنید.";
                return;
            }

            await using var db = SadrDb.New();
            var type = CmbType.SelectedValue as int? ?? InvoiceTypeConsts.Sell;
            var partyId = CmbParty.SelectedValue as int?;
            int? customerId = type == InvoiceTypeConsts.Buy ? null : partyId is > 0 ? partyId : null;
            int? providerId = type == InvoiceTypeConsts.Buy ? partyId is > 0 ? partyId : null : null;

            // Non-blocking stock check for sell invoices: warn when selling more than the
            // projected stock (opening + all confirmed/pending movements except this invoice's
            // own). The seller can still save — goods may be on the way, or the keeper may
            // not have accepted transfers yet.
            if (type == InvoiceTypeConsts.Sell)
            {
                var productIds = rows.Select(r => r.ProductId).Distinct().ToList();
                var prods = await db.Products.Where(p => productIds.Contains(p.Id))
                    .Select(p => new { p.Id, p.Name, p.InitialQuantity }).ToDictionaryAsync(p => p.Id);
                var selfId = _invoiceId;
                var others = await db.WarehouseTransfers
                    .Where(t => !t.Deleted && productIds.Contains(t.ProductId)
                                && (selfId == null || t.InvoiceId != selfId))
                    .Select(t => new { t.ProductId, t.Direction, t.Quantity })
                    .ToListAsync();
                var net = others.GroupBy(t => t.ProductId).ToDictionary(
                    g => g.Key, g => g.Sum(t => t.Direction == WarehouseTransfer.DirectionIn ? t.Quantity : -t.Quantity));
                var shortfalls = new List<string>();
                foreach (var g in rows.GroupBy(r => r.ProductId))
                {
                    var projected = (prods.TryGetValue(g.Key, out var pr) ? pr.InitialQuantity : 0m)
                                    + net.GetValueOrDefault(g.Key)
                                    - g.Sum(r => r.Amount);
                    if (projected < 0)
                        shortfalls.Add($"• {(prods.TryGetValue(g.Key, out var p2) ? p2.Name : "کالای #" + g.Key)}: کسری {(-projected):N0}");
                }
                if (shortfalls.Count > 0 && MessageBox.Show(
                        "موجودی پیش‌بینی‌شده کالاهای زیر کافی نیست:\n" + string.Join("\n", shortfalls) +
                        "\n\nبا این حال فاکتور ذخیره شود؟", "هشدار موجودی",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
            }

            Invoice inv;
            if (_invoiceId is int id)
            {
                inv = await db.Invoices.Include(i => i.Details).FirstAsync(i => i.Id == id);
                // soft-delete removed lines — matched by line identity, not product id,
                // so the same product can appear twice in one invoice
                var keptDetailIds = rows.Where(r => r.DetailId > 0).Select(r => r.DetailId).ToHashSet();
                foreach (var d in inv.Details.Where(x => !x.Deleted && !keptDetailIds.Contains(x.Id)))
                    d.Deleted = true;
            }
            else
            {
                inv = new Invoice { RecordUniqueId = Guid.NewGuid() };
                db.Invoices.Add(inv);
            }

            inv.InvoiceDateG = DpDate.Value.Value;
            inv.InvoiceDate = PersianDate.ToPersian(inv.InvoiceDateG);
            inv.InvoiceNumber = string.IsNullOrWhiteSpace(TxtNumber.Text) ? await NextNumber(db, type) : TxtNumber.Text.Trim();
            inv.Code = string.IsNullOrWhiteSpace(TxtCode.Text) ? null : TxtCode.Text.Trim();
            inv.Description = TxtDescription.Text ?? "";
            inv.InvoiceType = type;
            inv.Status = CmbStatus.SelectedValue as int? ?? InvoiceStatusConsts.Draft;
            inv.CustomerId = customerId;
            inv.ProviderId = providerId;
            inv.TotalPrice = rows.Sum(r => r.Total);
            inv.TotalDiscount = rows.Sum(r => r.DiscountRow);

            // upsert lines (matched by loaded line identity; rows with DetailId 0 are new lines)
            foreach (var r in rows)
            {
                var existing = _invoiceId is int && r.DetailId > 0
                    ? inv.Details.FirstOrDefault(d => d.Id == r.DetailId)
                    : null;
                if (existing is null)
                {
                    db.InvoiceDetails.Add(new InvoiceDetail
                    {
                        RecordUniqueId = Guid.NewGuid(),
                        Invoice = inv,
                        ProductId = r.ProductId,
                        PriceId = r.PriceId,
                        UnitPriceAmount = r.UnitPrice,
                        CurrencyId = r.CurrencyId,
                        UnitId = r.UnitId,
                        Amount = r.Amount,
                        ExtraPersentage = r.ExtraPercent,
                        DiscountPersentage = r.DiscountPercent,
                        Status = 0,
                        TotalDiscount = r.DiscountRow,
                        TotalPriceAmount = r.Total
                    });
                }
                else
                {
                    existing.ProductId = r.ProductId;
                    existing.UnitPriceAmount = r.UnitPrice;
                    existing.CurrencyId = r.CurrencyId;
                    existing.UnitId = r.UnitId;
                    existing.Amount = r.Amount;
                    existing.ExtraPersentage = r.ExtraPercent;
                    existing.DiscountPersentage = r.DiscountPercent;
                    existing.TotalDiscount = r.DiscountRow;
                    existing.TotalPriceAmount = r.Total;
                    existing.PriceId = r.PriceId;
                    existing.Deleted = false;
                }
            }

            await db.SaveChangesAsync();

            // Buy/sell invoices move goods: (re)generate one pending warehouse transfer per
            // product line; a warehouse keeper later accepts them with the real date.
            if (inv.InvoiceType == InvoiceTypeConsts.Buy || inv.InvoiceType == InvoiceTypeConsts.Sell)
                await WarehouseStock.SyncInvoiceTransfersAsync(db, inv, UserSession.CurrentUserId);

            SavedInvoiceId = inv.Id;

            // Offer to register the money movement right after saving (several
            // transactions per invoice are allowed via the money window).
            if (inv.InvoiceType != InvoiceTypeConsts.PreInvoice &&
                MessageBox.Show("برای این فاکتور تراکنش مالی (دریافت/پرداخت) ثبت شود؟",
                    "ثبت وجه", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                Hide();
                new InvoiceMoneyWindow(inv.Id, inv.InvoiceNumber).ShowDialog();
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "خطا در ذخیره فاکتور", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
