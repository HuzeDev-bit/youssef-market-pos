namespace MarketPos.Services;

/// <summary>
/// Every word the app says, in French and in Arabic.
///
/// Generated, and keyed by the English text itself - see <see cref="Loc"/> for why. The
/// English column is the source: change a label in the XAML and the old text simply stops
/// matching, which shows up as one untranslated line rather than as a silently wrong one.
///
/// Arabic is Modern Standard, which is what a Moroccan shop would print and what any customer
/// can read. French is the language most of this trade is actually done in.
/// </summary>
public static class Translations
{
    /// <summary>English to (French, Arabic).</summary>
    public static readonly IReadOnlyDictionary<string, (string Fr, string Ar)> Table =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            [" — take them off the shelf and record the loss."] =
                (" — retirez-les du rayon et enregistrez la perte.",
                 " — أزلها من الرف وسجّل الخسارة."),
            ["= gross profit"] =
                ("= bénéfice brut",
                 "= الربح الإجمالي"),
            ["= net profit"] =
                ("= bénéfice net",
                 "= الربح الصافي"),
            ["A product appears here once it drops to the smallest amount you set for it."] =
                ("Un produit apparaît ici dès qu'il descend au minimum que vous lui avez fixé.",
                 "يظهر المنتج هنا بمجرد أن ينزل إلى الحد الأدنى الذي حددته له."),
            ["A product with a printed barcode is scanned. One without gets a tile the cashier presses, which is where its photo shows. Turn this off for something the shop records but never sells over the counter."] =
                ("Un produit avec code-barres imprimé se scanne. Sans code-barres, il obtient une vignette que le caissier presse, et c'est là que s'affiche sa photo. Désactivez pour ce que la boutique enregistre mais ne vend jamais au comptoir.",
                 "المنتج الذي يحمل باركود مطبوع يُمسح ضوئياً. أما الذي بلا باركود فيحصل على مربع يضغطه الكاشير، وهناك تظهر صورته. أوقف هذا لما يسجله المتجر ولا يبيعه على الإطلاق."),
            ["A single emoji, used when there is no picture"] =
                ("Un seul emoji, utilisé s'il n'y a pas d'image",
                 "رمز تعبيري واحد، يُستخدم عند غياب الصورة"),
            ["ADDED"] =
                ("AJOUTÉ",
                 "أُضيف"),
            ["ADDRESS"] =
                ("ADRESSE",
                 "العنوان"),
            ["AMOUNT"] =
                ("MONTANT",
                 "المبلغ"),
            ["Activity log"] =
                ("Journal d'activité",
                 "سجل النشاط"),
            ["Add"] =
                ("Ajouter",
                 "إضافة"),
            ["Add category"] =
                ("Ajouter une catégorie",
                 "إضافة فئة"),
            ["Add expense"] =
                ("Ajouter une dépense",
                 "إضافة مصروف"),
            ["Add line"] =
                ("Ajouter une ligne",
                 "إضافة سطر"),
            ["Add or remove"] =
                ("Ajouter ou retirer",
                 "إضافة أو إزالة"),
            ["Add product"] =
                ("Ajouter un produit",
                 "إضافة منتج"),
            ["Add supplier"] =
                ("Ajouter un fournisseur",
                 "إضافة مورد"),
            ["Add the people who work here. Give one a password and they can open the back office as themselves."] =
                ("Ajoutez les personnes qui travaillent ici. Donnez un mot de passe à quelqu'un et il pourra ouvrir l'arrière-boutique en son nom.",
                 "أضف من يعملون هنا. امنح أحدهم كلمة مرور ليتمكن من فتح الإدارة باسمه."),
            ["Add the products that arrived."] =
                ("Ajoutez les produits qui sont arrivés.",
                 "أضف المنتجات التي وصلت."),
            ["Add the wholesalers the shop buys from. Once a delivery is recorded against one, what is owed to them shows up here."] =
                ("Ajoutez les grossistes chez qui la boutique achète. Dès qu'une livraison est enregistrée, ce qui leur est dû apparaît ici.",
                 "أضف تجار الجملة الذين يشتري منهم المتجر. وبمجرد تسجيل توصيل لأحدهم، يظهر ما هو مستحق له هنا."),
            ["Add what arrived, or leave it empty."] =
                ("Ajoutez ce qui est arrivé, ou laissez vide.",
                 "أضف ما وصل، أو اتركه فارغاً."),
            ["Add what the shop sells under Add product, and it will appear here."] =
                ("Saisissez ce que la boutique vend sous Ajouter un produit, et cela apparaîtra ici.",
                 "أدخل ما يبيعه المتجر تحت إضافة منتج، وسيظهر هنا."),
            ["Add worker"] =
                ("Ajouter un employé",
                 "إضافة موظف"),
            ["Adjust or count this stock"] =
                ("Ajuster ou compter ce stock",
                 "تعديل أو جرد هذا المخزون"),
            ["Amount  DH"] =
                ("Montant  DH",
                 "المبلغ  درهم"),
            ["Another product already uses that barcode."] =
                ("Un autre produit utilise déjà ce code-barres.",
                 "منتج آخر يستخدم هذا الباركود بالفعل."),
            ["Any cashier"] =
                ("Tous les caissiers",
                 "كل الكاشيرات"),
            ["Apply"] =
                ("Appliquer",
                 "تطبيق"),
            ["Attach"] =
                ("Joindre",
                 "إرفاق"),
            ["BACK OFFICE ADDRESS"] =
                ("ADRESSE DE L'ARRIÈRE-BOUTIQUE",
                 "عنوان جهاز الإدارة"),
            ["BARCODE"] =
                ("CODE-BARRES",
                 "الباركود"),
            ["BIGGEST"] =
                ("LE PLUS GROS",
                 "الأكبر"),
            ["BILLS AND WAGES"] =
                ("FACTURES ET SALAIRES",
                 "الفواتير والأجور"),
            ["BOUGHT"] =
                ("ACHETÉ",
                 "المشتريات"),
            ["BOUGHT FOR"] =
                ("ACHETÉ À",
                 "اشتُري بـ"),
            ["BUSINESS NAME"] =
                ("RAISON SOCIALE",
                 "اسم النشاط"),
            ["Back"] =
                ("Retour",
                 "رجوع"),
            ["Back office"] =
                ("Arrière-boutique",
                 "الإدارة"),
            ["Back office — {0}"] =
                ("Arrière-boutique — {0}",
                 "الإدارة — {0}"),
            ["Back to categories"] =
                ("Retour aux catégories",
                 "العودة إلى الفئات"),
            ["Back to the list"] =
                ("Retour à la liste",
                 "العودة إلى القائمة"),
            ["Back to the till"] =
                ("Retour à la caisse",
                 "العودة إلى الصندوق"),
            ["By weight (kg)"] =
                ("Au poids (kg)",
                 "بالوزن (كغ)"),
            ["CASHIER"] =
                ("CAISSIER",
                 "الكاشير"),
            ["CATEGORIES"] =
                ("CATÉGORIES",
                 "الفئات"),
            ["CATEGORY"] =
                ("CATÉGORIE",
                 "الفئة"),
            ["CONFIRM PASSWORD"] =
                ("CONFIRMER LE MOT DE PASSE",
                 "تأكيد كلمة المرور"),
            ["CONFIRM PIN"] =
                ("CONFIRMER LE CODE",
                 "تأكيد الرمز"),
            ["CONTACT PERSON"] =
                ("PERSONNE À CONTACTER",
                 "الشخص المسؤول"),
            ["COST"] =
                ("COÛT",
                 "التكلفة"),
            ["COST EACH"] =
                ("COÛT UNITAIRE",
                 "تكلفة الوحدة"),
            ["COST OF WHAT SOLD"] =
                ("COÛT DE CE QUI EST VENDU",
                 "تكلفة ما بيع"),
            ["COST TO BUY"] =
                ("COÛT D'ACHAT",
                 "تكلفة الشراء"),
            ["CURRENCY"] =
                ("DEVISE",
                 "العملة"),
            ["Cancel"] =
                ("Annuler",
                 "إلغاء"),
            ["Cancel sale"] =
                ("Annuler la vente",
                 "إلغاء البيع"),
            ["Cannot see profit, salaries, supplier debt or settings."] =
                ("Ne voit ni le bénéfice, ni les salaires, ni la dette fournisseurs, ni les réglages.",
                 "لا يرى الأرباح ولا الرواتب ولا ديون الموردين ولا الإعدادات."),
            ["Cashiers browse these to find products that have no barcode."] =
                ("Les caissiers les parcourent pour trouver les produits sans code-barres.",
                 "يتصفحها الكاشيرات للعثور على المنتجات التي بلا باركود."),
            ["Categories"] =
                ("Catégories",
                 "الفئات"),
            ["Categories are how a cashier finds something with no barcode. Add one for each kind of shelf."] =
                ("Les catégories permettent au caissier de trouver ce qui n'a pas de code-barres. Ajoutez-en une par type de rayon.",
                 "الفئات هي وسيلة الكاشير للعثور على ما لا يحمل باركود. أضف فئة لكل نوع من الرفوف."),
            ["Change it, or cancel it"] =
                ("Le modifier, ou l'annuler",
                 "تعديله أو إلغاؤه"),
            ["Change the period above, or ring something up at the till — the best sellers appear here."] =
                ("Changez la période ci-dessus, ou encaissez quelque chose en caisse — les meilleures ventes apparaissent ici.",
                 "غيّر الفترة أعلاه، أو سجّل عملية بيع في الصندوق — وستظهر الأكثر مبيعاً هنا."),
            ["Choose a photo for this product"] =
                ("Choisir une photo pour ce produit",
                 "اختر صورة لهذا المنتج"),
            ["Choose a picture"] =
                ("Choisir une image",
                 "اختر صورة"),
            ["Choose or type a category."] =
                ("Choisissez ou saisissez une catégorie.",
                 "اختر فئة أو اكتبها."),
            ["Clear search"] =
                ("Effacer la recherche",
                 "مسح البحث"),
            ["Close"] =
                ("Fermer",
                 "إغلاق"),
            ["Close the back office"] =
                ("Fermer l'arrière-boutique",
                 "إغلاق الإدارة"),
            ["Close the till"] =
                ("Fermer la caisse",
                 "إغلاق الصندوق"),
            ["Completed sales appear here. Finish a sale with Pay and its ticket lands in this list."] =
                ("Les ventes terminées apparaissent ici. Terminez une vente avec Payer et son ticket arrive dans cette liste.",
                 "تظهر المبيعات المكتملة هنا. أنهِ عملية بيع بالضغط على الدفع وستصل تذكرتها إلى هذه القائمة."),
            ["Connected"] =
                ("Connecté",
                 "متصل"),
            ["Could not save the sale: {0}"] =
                ("Impossible d'enregistrer la vente : {0}",
                 "تعذّر حفظ عملية البيع: {0}"),
            ["Current Sale"] =
                ("Vente en cours",
                 "البيع الحالي"),
            ["Custom"] =
                ("Personnalisé",
                 "مخصص"),
            ["DAILY"] =
                ("QUOTIDIEN",
                 "يومي"),
            ["DATE"] =
                ("DATE",
                 "التاريخ"),
            ["DELIVERED ON"] =
                ("LIVRÉ LE",
                 "سُلم في"),
            ["DELIVERIES"] =
                ("LIVRAISONS",
                 "التوصيلات"),
            ["DELIVERY TOTAL"] =
                ("TOTAL DE LA LIVRAISON",
                 "مجموع التوصيل"),
            ["DUE"] =
                ("DÛ",
                 "المستحق"),
            ["Dashboard"] =
                ("Tableau de bord",
                 "لوحة القيادة"),
            ["Deactivate"] =
                ("Désactiver",
                 "تعطيل"),
            ["Discard this ticket"] =
                ("Supprimer ce ticket",
                 "حذف هذه التذكرة"),
            ["Discount for a regular customer"] =
                ("Remise pour un client fidèle",
                 "تخفيض لزبون دائم"),
            ["Discount percentage"] =
                ("Pourcentage de remise",
                 "نسبة التخفيض"),
            ["Done"] =
                ("Terminé",
                 "تم"),
            ["EMAIL"] =
                ("E-MAIL",
                 "البريد الإلكتروني"),
            ["EVERY MONTH"] =
                ("CHAQUE MOIS",
                 "كل شهر"),
            ["EXPIRES ON"] =
                ("EXPIRE LE",
                 "ينتهي في"),
            ["Edit their details"] =
                ("Modifier leurs informations",
                 "تعديل بياناتهم"),
            ["Edit this product — price, photo, barcode"] =
                ("Modifier ce produit — prix, photo, code-barres",
                 "تعديل هذا المنتج — السعر والصورة والباركود"),
            ["Enter a barcode, or press Generate for an in-store code."] =
                ("Saisissez un code-barres, ou appuyez sur Générer pour un code interne.",
                 "أدخل باركود، أو اضغط توليد للحصول على رمز داخلي."),
            ["Enter a receipt number to see it here"] =
                ("Saisissez un numéro de ticket pour le voir ici",
                 "أدخل رقم إيصال لعرضه هنا"),
            ["Enter the receipt number printed on a completed sale. Nothing is charged again."] =
                ("Saisissez le numéro imprimé sur un ticket déjà encaissé. Rien n'est facturé à nouveau.",
                 "أدخل رقم الإيصال المطبوع على بيع مكتمل. لن يُحصّل أي مبلغ من جديد."),
            ["Enter what it sells for."] =
                ("Indiquez son prix de vente.",
                 "أدخل سعر بيعه."),
            ["Every amount needs a currency after it."] =
                ("Chaque montant a besoin d'une devise.",
                 "كل مبلغ يحتاج عملة بعده."),
            ["Every kind"] =
                ("Tous les types",
                 "كل الأنواع"),
            ["Every receipt, and what it made"] =
                ("Chaque ticket, et ce qu'il a rapporté",
                 "كل إيصال، وما حققه"),
            ["Everything"] =
                ("Tout",
                 "كل شيء"),
            ["Everything in the shop has a barcode, so there is nothing to press. Bread, produce and anything else without one appears here."] =
                ("Tout dans la boutique a un code-barres, il n'y a donc rien à presser. Le pain, les fruits et légumes et tout ce qui n'en a pas apparaissent ici.",
                 "كل ما في المتجر يحمل باركود، فلا شيء للضغط عليه. الخبز والخضر وكل ما لا يحمل باركود يظهر هنا."),
            ["Everything on one invoice goes in together. Stock goes up when you save; money only leaves for what you actually pay."] =
                ("Tout ce qui est sur une facture s'enregistre ensemble. Le stock augmente à l'enregistrement ; l'argent ne sort que pour ce que vous payez réellement.",
                 "كل ما في فاتورة واحدة يُسجَّل معاً. يرتفع المخزون عند الحفظ؛ ولا يخرج المال إلا مقابل ما تدفعه فعلاً."),
            ["Everything, including profit, salaries, supplier debt and business settings."] =
                ("Tout, y compris le bénéfice, les salaires, la dette fournisseurs et les réglages.",
                 "كل شيء، بما في ذلك الأرباح والرواتب وديون الموردين وإعدادات النشاط."),
            ["Expenses"] =
                ("Dépenses",
                 "المصاريف"),
            ["Export CSV"] =
                ("Exporter en CSV",
                 "تصدير CSV"),
            ["Find"] =
                ("Chercher",
                 "بحث"),
            ["GROSS PROFIT"] =
                ("BÉNÉFICE BRUT",
                 "الربح الإجمالي"),
            ["Generate"] =
                ("Générer",
                 "توليد"),
            ["Give it an in-store code instead"] =
                ("Lui donner un code interne à la place",
                 "امنحه رمزاً داخلياً بدلاً من ذلك"),
            ["Give the product a name."] =
                ("Donnez un nom au produit.",
                 "امنح المنتج اسماً."),
            ["Give the shop a name — it goes on every receipt."] =
                ("Donnez un nom à la boutique — il figure sur chaque ticket.",
                 "امنح المتجر اسماً — فهو يظهر على كل إيصال."),
            ["Give them a role now; a till PIN can be set afterwards."] =
                ("Donnez-leur un rôle maintenant ; le code de caisse se règle ensuite.",
                 "امنحهم دوراً الآن؛ ويمكن تعيين رمز الصندوق لاحقاً."),
            ["Go to where this is fixed"] =
                ("Aller là où cela se règle",
                 "الذهاب إلى حيث يُصلح هذا"),
            ["Hidden"] =
                ("Masqué",
                 "مخفي"),
            ["Hold ticket"] =
                ("Mettre en attente",
                 "تعليق التذكرة"),
            ["How products are grouped on the till"] =
                ("Comment les produits sont regroupés en caisse",
                 "كيف تُجمَّع المنتجات في الصندوق"),
            ["How the shop is doing"] =
                ("Comment va la boutique",
                 "كيف حال المتجر"),
            ["ICON"] =
                ("ICÔNE",
                 "أيقونة"),
            ["IN STOCK"] =
                ("EN STOCK",
                 "في المخزون"),
            ["INSIGHT"] =
                ("ANALYSE",
                 "التحليل"),
            ["INVOICE NUMBER"] =
                ("NUMÉRO DE FACTURE",
                 "رقم الفاتورة"),
            ["INVOICE TOTAL"] =
                ("TOTAL DE LA FACTURE",
                 "مجموع الفاتورة"),
            ["ITEM"] =
                ("ARTICLE",
                 "المنتج"),
            ["ITEMS"] =
                ("ARTICLES",
                 "المنتجات"),
            ["ITEMS SOLD"] =
                ("ARTICLES VENDUS",
                 "المنتجات المباعة"),
            ["Inventory"] =
                ("Stock",
                 "المخزون"),
            ["Invoice {0} was due {1} day ago."] =
                ("La facture {0} était due il y a {1} jour.",
                 "الفاتورة {0} كانت مستحقة منذ يوم."),
            ["Invoice {0} was due {1} days ago."] =
                ("La facture {0} était due il y a {1} jours.",
                 "الفاتورة {0} كانت مستحقة منذ {1} أيام."),
            ["Invoice {0} — no items listed"] =
                ("Facture {0} — aucun article listé",
                 "الفاتورة {0} — لا توجد منتجات مدرجة"),
            ["It goes on the till as soon as you save."] =
                ("Il arrive en caisse dès l'enregistrement.",
                 "يصل إلى الصندوق بمجرد الحفظ."),
            ["KEPT AS PROFIT"] =
                ("GARDÉ EN BÉNÉFICE",
                 "المحتفظ به كربح"),
            ["KIND"] =
                ("TYPE",
                 "النوع"),
            ["LANGUAGE"] =
                ("LANGUE",
                 "اللغة"),
            ["LEFT"] =
                ("RESTE",
                 "المتبقي"),
            ["Leave empty if this is the only computer in the shop. Fill it in on a second till, and it will keep selling even when the back office is off — sales catch up when it comes back."] =
                ("Laissez vide s'il s'agit du seul ordinateur de la boutique. Renseignez-le sur une deuxième caisse : elle continuera à vendre même si l'arrière-boutique est éteinte — les ventes se rattrapent à son retour.",
                 "اتركه فارغاً إن كان هذا هو الحاسوب الوحيد في المتجر. املأه في صندوق ثانٍ، وسيواصل البيع حتى وإن كان جهاز الإدارة مطفأً — وتلحق المبيعات عند عودته."),
            ["Leave this off when the goods came back damaged or opened — the stock is gone either way, and ticking it would put items back that cannot be sold."] =
                ("Laissez décoché si la marchandise est revenue abîmée ou ouverte — le stock est perdu de toute façon, et cocher remettrait en rayon des articles invendables.",
                 "اترك هذا دون تحديد إذا عادت البضاعة تالفة أو مفتوحة — المخزون ضائع في الحالتين، وتحديده سيعيد إلى الرف منتجات لا يمكن بيعها."),
            ["Low"] =
                ("Bas",
                 "منخفض"),
            ["MARGIN"] =
                ("MARGE",
                 "الهامش"),
            ["MIN"] =
                ("MIN",
                 "الأدنى"),
            ["MINIMUM STOCK"] =
                ("STOCK MINIMUM",
                 "الحد الأدنى للمخزون"),
            ["MONEY"] =
                ("ARGENT",
                 "المال"),
            ["Make an in-store code for a product with no printed barcode"] =
                ("Créer un code interne pour un produit sans code-barres imprimé",
                 "إنشاء رمز داخلي لمنتج بدون باركود مطبوع"),
            ["Manages products and stock levels, and can see stock movements. No money screens."] =
                ("Gère les produits et les niveaux de stock, et voit les mouvements. Aucun écran d'argent.",
                 "يدير المنتجات ومستويات المخزون ويرى الحركات. لا شاشات مالية."),
            ["Minimise"] =
                ("Réduire",
                 "تصغير"),
            ["Minimize"] =
                ("Réduire",
                 "تصغير"),
            ["Money that left the shop"] =
                ("Argent sorti de la boutique",
                 "المال الذي خرج من المتجر"),
            ["NAME"] =
                ("NOM",
                 "الاسم"),
            ["NEEDS ATTENTION"] =
                ("À TRAITER",
                 "يحتاج انتباهك"),
            ["NEEDS REORDERING"] =
                ("À RECOMMANDER",
                 "يحتاج إعادة طلب"),
            ["NET PROFIT"] =
                ("BÉNÉFICE NET",
                 "الربح الصافي"),
            ["NOT IN A CATEGORY"] =
                ("SANS CATÉGORIE",
                 "بدون فئة"),
            ["NOTE (OPTIONAL)"] =
                ("NOTE (FACULTATIF)",
                 "ملاحظة (اختياري)"),
            ["NOTES"] =
                ("NOTES",
                 "ملاحظات"),
            ["New product"] =
                ("Nouveau produit",
                 "منتج جديد"),
            ["No"] =
                ("Non",
                 "لا"),
            ["No admin password is set, so anyone at this machine can open the back office. You can set one under Settings → Access."] =
                ("Aucun mot de passe administrateur n'est défini : n'importe qui sur cette machine peut ouvrir l'arrière-boutique. Vous pouvez en définir un sous Réglages → Accès.",
                 "لم تُحدَّد كلمة مرور للإدارة، فبإمكان أي شخص على هذا الجهاز فتحها. يمكنك تعيين واحدة من الإعدادات ← الوصول."),
            ["No barcode"] =
                ("Sans code-barres",
                 "بدون باركود"),
            ["No bills recorded"] =
                ("Aucune facture enregistrée",
                 "لا فواتير مسجلة"),
            ["No categories yet"] =
                ("Aucune catégorie",
                 "لا توجد فئات بعد"),
            ["No items listed"] =
                ("Aucun article listé",
                 "لا توجد منتجات مدرجة"),
            ["No lines yet. Name what arrived - pick it from the list, or type a new one - then how many came, what each cost, and what it sells for."] =
                ("Aucune ligne. Nommez ce qui est arrivé - choisissez-le dans la liste, ou saisissez-en un nouveau - puis combien il en est venu, le coût unitaire, et le prix de vente.",
                 "لا توجد سطور بعد. سمِّ ما وصل - اخترْه من القائمة أو اكتب اسماً جديداً - ثم كم وصل، وكم كلّف كل واحد، وبكم يُباع."),
            ["No overdue bills, no empty shelves, nothing about to go off."] =
                ("Aucune facture en retard, aucun rayon vide, rien qui approche de sa date.",
                 "لا فواتير متأخرة، ولا رفوف فارغة، ولا شيء يوشك على انتهاء صلاحيته."),
            ["No products found"] =
                ("Aucun produit trouvé",
                 "لم يُعثر على منتجات"),
            ["No products yet"] =
                ("Aucun produit",
                 "لا توجد منتجات بعد"),
            ["No returns"] =
                ("Aucun retour",
                 "لا مرتجعات"),
            ["No salary recorded for this month yet."] =
                ("Aucun salaire enregistré pour ce mois.",
                 "لم يُسجَّل أي راتب لهذا الشهر بعد."),
            ["No sales in this period"] =
                ("Aucune vente sur cette période",
                 "لا مبيعات في هذه الفترة"),
            ["No sales yet"] =
                ("Aucune vente",
                 "لا مبيعات بعد"),
            ["No staff yet"] =
                ("Aucun employé",
                 "لا يوجد موظفون بعد"),
            ["No suppliers yet"] =
                ("Aucun fournisseur",
                 "لا يوجد موردون بعد"),
            ["No tickets yet"] =
                ("Aucun ticket",
                 "لا توجد تذاكر بعد"),
            ["No worker has a password yet, so only the owner can open the back office."] =
                ("Aucun employé n'a encore de mot de passe : seul le propriétaire peut ouvrir l'arrière-boutique.",
                 "لا يملك أي موظف كلمة مرور بعد، لذا لا يمكن فتح الإدارة إلا للمالك."),
            ["None attached"] =
                ("Aucun justificatif",
                 "لا يوجد مرفق"),
            ["Nothing added yet"] =
                ("Rien d'ajouté pour l'instant",
                 "لم يُضف شيء بعد"),
            ["Nothing bought from them yet."] =
                ("Rien acheté chez eux pour l'instant.",
                 "لم يُشترَ منهم شيء بعد."),
            ["Nothing happened"] =
                ("Rien ne s'est passé",
                 "لم يحدث شيء"),
            ["Nothing here is called “{0}”"] =
                ("Rien ici ne s'appelle « {0} »",
                 "لا شيء هنا اسمه «{0}»"),
            ["Nothing here matches what you typed, or the category filter is hiding it."] =
                ("Rien ici ne correspond à votre saisie, ou le filtre de catégorie le masque.",
                 "لا شيء هنا يطابق ما كتبته، أو أن مرشّح الفئة يخفيه."),
            ["Nothing in the shop yet"] =
                ("La boutique est vide",
                 "لا شيء في المتجر بعد"),
            ["Nothing in this period"] =
                ("Rien sur cette période",
                 "لا شيء في هذه الفترة"),
            ["Nothing is overdue or running out."] =
                ("Rien n'est en retard ni sur le point de manquer.",
                 "لا شيء متأخر ولا على وشك النفاد."),
            ["Nothing matches"] =
                ("Aucun résultat",
                 "لا توجد نتائج"),
            ["Nothing matches \"{0}\""] =
                ("Rien ne correspond à « {0} »",
                 "لا شيء يطابق «{0}»"),
            ["Nothing needs you"] =
                ("Rien ne vous attend",
                 "لا شيء يحتاجك"),
            ["Nothing paid yet."] =
                ("Aucun paiement pour l'instant.",
                 "لم يُدفع شيء بعد."),
            ["Nothing recorded yet."] =
                ("Rien d'enregistré pour l'instant.",
                 "لم يُسجَّل شيء بعد."),
            ["Nothing sold"] =
                ("Rien de vendu",
                 "لم يُبع شيء"),
            ["Nothing sold yet in this period"] =
                ("Rien de vendu sur cette période",
                 "لم يُبع شيء في هذه الفترة"),
            ["Nothing spent in this period."] =
                ("Aucune dépense sur cette période.",
                 "لا مصاريف في هذه الفترة."),
            ["Nothing to reorder"] =
                ("Rien à recommander",
                 "لا شيء لإعادة طلبه"),
            ["Nothing to sell yet"] =
                ("Rien à vendre pour l'instant",
                 "لا شيء للبيع بعد"),
            ["OFF THE SHELF"] =
                ("SORTIS DU RAYON",
                 "خرج من الرف"),
            ["ON HOLD"] =
                ("EN ATTENTE",
                 "في الانتظار"),
            ["OPENING STOCK"] =
                ("STOCK DE DÉPART",
                 "المخزون الافتتاحي"),
            ["OWED"] =
                ("DETTE",
                 "الدين"),
            ["Only ones I owe"] =
                ("Seulement ceux que je dois",
                 "فقط الذين لي عليهم دين"),
            ["Only the name is required. Put in what they brought below and it is recorded with them."] =
                ("Seul le nom est obligatoire. Saisissez ci-dessous ce qu'ils ont livré et cela leur est rattaché.",
                 "الاسم وحده مطلوب. أدخل أدناه ما أحضروه وسيُسجَّل باسمهم."),
            ["Open the inventory"] =
                ("Ouvrir le stock",
                 "فتح المخزون"),
            ["Optional. Stock goes up when you save, and whatever you do not pay becomes what you owe them."] =
                ("Facultatif. Le stock augmente à l'enregistrement, et ce que vous ne payez pas devient votre dette envers eux.",
                 "اختياري. يرتفع المخزون عند الحفظ، وما لا تدفعه يصبح ديناً عليك لهم."),
            ["Optional. This product is scanned, so the photo only shows on receipts and lists."] =
                ("Facultatif. Ce produit se scanne, la photo n'apparaît donc que sur les tickets et les listes.",
                 "اختياري. هذا المنتج يُمسح ضوئياً، فالصورة تظهر على الإيصالات والقوائم فقط."),
            ["Optional. Without one the card shows the icon, or the category's initial."] =
                ("Facultatif. Sans image, la carte affiche l'icône, ou l'initiale de la catégorie.",
                 "اختياري. بدونها تعرض البطاقة الأيقونة أو الحرف الأول للفئة."),
            ["Out"] =
                ("Rupture",
                 "نفد"),
            ["Over 100%"] =
                ("Plus de 100 %",
                 "أكثر من 100%"),
            ["Owner only"] =
                ("Réservé au propriétaire",
                 "للمالك فقط"),
            ["PAID"] =
                ("PAYÉ",
                 "مدفوع"),
            ["PAID BY"] =
                ("PAYÉ PAR",
                 "دفعه"),
            ["PAID NOW"] =
                ("PAYÉ MAINTENANT",
                 "مدفوع الآن"),
            ["PASSWORD"] =
                ("MOT DE PASSE",
                 "كلمة المرور"),
            ["PAYMENT"] =
                ("PAIEMENT",
                 "الدفع"),
            ["PAYMENT DUE"] =
                ("PAIEMENT DÛ",
                 "الدفع المستحق"),
            ["PAYMENTS"] =
                ("PAIEMENTS",
                 "المدفوعات"),
            ["PEOPLE"] =
                ("PERSONNEL",
                 "الموظفون"),
            ["PHONE"] =
                ("TÉLÉPHONE",
                 "الهاتف"),
            ["PHOTO"] =
                ("PHOTO",
                 "صورة"),
            ["PICTURE"] =
                ("IMAGE",
                 "صورة"),
            ["PIN"] =
                ("CODE PIN",
                 "الرمز السري"),
            ["PRICE"] =
                ("PRIX",
                 "السعر"),
            ["PRICE CHECK"] =
                ("VÉRIFIER LE PRIX",
                 "التحقق من السعر"),
            ["PRODUCT"] =
                ("PRODUIT",
                 "المنتج"),
            ["PRODUCT NAME"] =
                ("NOM DU PRODUIT",
                 "اسم المنتج"),
            ["PRODUCTS GROUPED"] =
                ("PRODUITS REGROUPÉS",
                 "المنتجات المجمعة"),
            ["PROFIT"] =
                ("BÉNÉFICE",
                 "الربح"),
            ["PROFIT PER SALE"] =
                ("BÉNÉFICE PAR VENTE",
                 "الربح لكل عملية"),
            ["PURCHASE PRICE (COST)"] =
                ("PRIX D'ACHAT (COÛT)",
                 "سعر الشراء (التكلفة)"),
            ["Paid up. {0} bought all told."] =
                ("Soldé. {0} achetés en tout.",
                 "مسدَّد. {0} مشتراة إجمالاً."),
            ["Pay"] =
                ("Payer",
                 "الدفع"),
            ["Pay wages"] =
                ("Payer les salaires",
                 "دفع الأجور"),
            ["Payment confirmed"] =
                ("Paiement confirmé",
                 "تم تأكيد الدفع"),
            ["Per unit"] =
                ("À l'unité",
                 "بالوحدة"),
            ["Percent  %"] =
                ("Pourcentage  %",
                 "النسبة  %"),
            ["Photo"] =
                ("Photo",
                 "صورة"),
            ["Pick a category to see what is in it."] =
                ("Choisissez une catégorie pour voir ce qu'elle contient.",
                 "اختر فئة لعرض ما بداخلها."),
            ["Pick a supplier"] =
                ("Choisir un fournisseur",
                 "اختر مورداً"),
            ["Pick a wider date range above, or ring up a sale on the till."] =
                ("Choisissez une période plus large, ou encaissez une vente en caisse.",
                 "اختر فترة أوسع أعلاه، أو سجّل عملية بيع في الصندوق."),
            ["Picture"] =
                ("Image",
                 "صورة"),
            ["Point the scanner at the barcode. You can also type it in below."] =
                ("Visez le code-barres avec le scanner. Vous pouvez aussi le saisir ci-dessous.",
                 "وجّه الماسح نحو الباركود. يمكنك أيضاً كتابته بالأسفل."),
            ["Press Add product, scan the box in your hand, and it appears here."] =
                ("Appuyez sur Ajouter un produit, scannez la boîte que vous avez en main, et elle apparaît ici.",
                 "اضغط على إضافة منتج، وامسح العلبة التي بيدك، وستظهر هنا."),
            ["Press to send now"] =
                ("Appuyez pour envoyer maintenant",
                 "اضغط للإرسال الآن"),
            ["Price check"] =
                ("Vérifier le prix",
                 "التحقق من السعر"),
            ["Print another copy of a past receipt"] =
                ("Imprimer une copie d'un ancien ticket",
                 "طباعة نسخة من إيصال سابق"),
            ["Print the receipt automatically after each sale"] =
                ("Imprimer le ticket automatiquement après chaque vente",
                 "طباعة الإيصال تلقائياً بعد كل عملية بيع"),
            ["Product name or barcode"] =
                ("Nom du produit ou code-barres",
                 "اسم المنتج أو الباركود"),
            ["Products"] =
                ("Produits",
                 "المنتجات"),
            ["Products are added in the back office, under Add product. Once they are in, they show up here and scan at the counter."] =
                ("Les produits s'ajoutent dans l'arrière-boutique, sous Ajouter un produit. Une fois saisis, ils apparaissent ici et se scannent au comptoir.",
                 "تُضاف المنتجات من الإدارة، تحت إضافة منتج. وبمجرد إدخالها تظهر هنا وتُمسح ضوئياً عند المنضدة."),
            ["Products in this category"] =
                ("Produits de cette catégorie",
                 "منتجات هذه الفئة"),
            ["Purchase prices missing"] =
                ("Prix d'achat manquants",
                 "أسعار الشراء ناقصة"),
            ["Put goods into the shop"] =
                ("Faire entrer la marchandise",
                 "إدخال البضاعة إلى المتجر"),
            ["Put the items back on the shelf"] =
                ("Remettre les articles en rayon",
                 "إعادة المنتجات إلى الرف"),
            ["Put this month's in"] =
                ("Saisir celle de ce mois",
                 "أدخل مصروف هذا الشهر"),
            ["Put what the shop sells in under Add product, and every sale will be counted here."] =
                ("Saisissez ce que la boutique vend sous Ajouter un produit, et chaque vente sera comptée ici.",
                 "أدخل ما يبيعه المتجر تحت إضافة منتج، وستُحتسب كل عملية بيع هنا."),
            ["QTY"] =
                ("QTÉ",
                 "الكمية"),
            ["QUANTITY"] =
                ("QUANTITÉ",
                 "الكمية"),
            ["REASON"] =
                ("MOTIF",
                 "السبب"),
            ["RECEIPT"] =
                ("TICKET",
                 "الإيصال"),
            ["RECEIPT FOOTER"] =
                ("PIED DE TICKET",
                 "تذييل الإيصال"),
            ["RECEIPT PHOTO"] =
                ("PHOTO DU JUSTIFICATIF",
                 "صورة الإيصال"),
            ["RECEIPT PRINTER"] =
                ("IMPRIMANTE À TICKETS",
                 "طابعة الإيصالات"),
            ["RECENT RECEIPTS"] =
                ("TICKETS RÉCENTS",
                 "الإيصالات الأخيرة"),
            ["REFUNDED"] =
                ("REMBOURSÉ",
                 "المسترجع"),
            ["REPEATS"] =
                ("RÉCURRENT",
                 "متكرر"),
            ["REVENUE"] =
                ("CHIFFRE D'AFFAIRES",
                 "المداخيل"),
            ["ROLE"] =
                ("RÔLE",
                 "الدور"),
            ["Read top to bottom. Each line takes something off the one above it, and the last line is what the shop actually kept."] =
                ("À lire de haut en bas. Chaque ligne retire quelque chose à celle du dessus, et la dernière ligne est ce que la boutique a réellement gardé.",
                 "اقرأ من الأعلى إلى الأسفل. كل سطر يطرح شيئاً من السطر الذي فوقه، والسطر الأخير هو ما احتفظ به المتجر فعلاً."),
            ["Receipt number, product or cashier"] =
                ("Numéro de ticket, produit ou caissier",
                 "رقم الإيصال أو المنتج أو الكاشير"),
            ["Record a delivery"] =
                ("Enregistrer une livraison",
                 "تسجيل توصيل"),
            ["Record a payment"] =
                ("Enregistrer un paiement",
                 "تسجيل دفعة"),
            ["Refund"] =
                ("Rembourser",
                 "استرجاع"),
            ["Reload"] =
                ("Recharger",
                 "تحديث"),
            ["Remise"] =
                ("Remise",
                 "تخفيض"),
            ["Remise ({0} DH)"] =
                ("Remise ({0} DH)",
                 "تخفيض ({0} درهم)"),
            ["Remise ({0}%)"] =
                ("Remise ({0} %)",
                 "تخفيض ({0}%)"),
            ["Remove"] =
                ("Supprimer",
                 "حذف"),
            ["Remove photo"] =
                ("Retirer la photo",
                 "إزالة الصورة"),
            ["Remove picture"] =
                ("Retirer l'image",
                 "إزالة الصورة"),
            ["Remove this line"] =
                ("Supprimer cette ligne",
                 "حذف هذا السطر"),
            ["Rename it, change its picture, or hide it"] =
                ("Le renommer, changer son image, ou le masquer",
                 "إعادة تسميتها أو تغيير صورتها أو إخفاؤها"),
            ["Rent, electricity, water, repairs — anything that is not stock."] =
                ("Loyer, électricité, eau, réparations — tout ce qui n'est pas du stock.",
                 "الكراء والكهرباء والماء والإصلاحات — كل ما ليس مخزوناً."),
            ["Rent, light, water, internet — everything that is not stock"] =
                ("Loyer, électricité, eau, internet — tout ce qui n'est pas du stock",
                 "الكراء والكهرباء والماء والإنترنت — كل ما ليس مخزوناً"),
            ["Reports"] =
                ("Rapports",
                 "التقارير"),
            ["Reprint"] =
                ("Réimprimer",
                 "إعادة الطباعة"),
            ["Reprint Receipt"] =
                ("Réimprimer le ticket",
                 "إعادة طباعة الإيصال"),
            ["Reprint receipt"] =
                ("Réimprimer le ticket",
                 "إعادة طباعة الإيصال"),
            ["Restart now"] =
                ("Redémarrer maintenant",
                 "أعد التشغيل الآن"),
            ["Running out"] =
                ("Bientôt épuisé",
                 "على وشك النفاد"),
            ["Runs the shop floor: products, stock, suppliers, purchases, staff and reports."] =
                ("Gère la boutique : produits, stock, fournisseurs, achats, personnel et rapports.",
                 "يدير المتجر: المنتجات والمخزون والموردين والمشتريات والموظفين والتقارير."),
            ["SALARY"] =
                ("SALAIRE",
                 "الراتب"),
            ["SALES"] =
                ("VENTES",
                 "المبيعات"),
            ["SELL FOR"] =
                ("VENDRE À",
                 "البيع بـ"),
            ["SELLING FOR"] =
                ("VENDU À",
                 "يُباع بـ"),
            ["SELLING PRICE"] =
                ("PRIX DE VENTE",
                 "سعر البيع"),
            ["SELLS FOR"] =
                ("SE VEND À",
                 "يُباع بـ"),
            ["SHARE OF SALES"] =
                ("PART DES VENTES",
                 "حصة المبيعات"),
            ["SHELF / LOCATION"] =
                ("RAYON / EMPLACEMENT",
                 "الرف / الموقع"),
            ["SHOP NAME"] =
                ("NOM DE LA BOUTIQUE",
                 "اسم المتجر"),
            ["SKU / INTERNAL CODE"] =
                ("SKU / CODE INTERNE",
                 "رمز داخلي"),
            ["SOLD"] =
                ("VENDU",
                 "البيع"),
            ["SOLD BY"] =
                ("VENDU PAR",
                 "يُباع بـ"),
            ["SPENT"] =
                ("DÉPENSÉ",
                 "المصروف"),
            ["STAFF"] =
                ("PERSONNEL",
                 "الموظفون"),
            ["STARTED ON"] =
                ("A COMMENCÉ LE",
                 "بدأ في"),
            ["STATUS"] =
                ("ÉTAT",
                 "الحالة"),
            ["STILL OWED"] =
                ("RESTE DÛ",
                 "ما زال مستحقاً"),
            ["STOCK"] =
                ("STOCK",
                 "المخزون"),
            ["STOCK IN THEM"] =
                ("STOCK CHEZ EUX",
                 "المخزون منهم"),
            ["SUPPLIER"] =
                ("FOURNISSEUR",
                 "المورد"),
            ["SUPPLIERS"] =
                ("FOURNISSEURS",
                 "الموردون"),
            ["Sale"] =
                ("Vente",
                 "بيع"),
            ["Sales history"] =
                ("Historique des ventes",
                 "سجل المبيعات"),
            ["Save"] =
                ("Enregistrer",
                 "حفظ"),
            ["Save delivery"] =
                ("Enregistrer la livraison",
                 "حفظ التوصيل"),
            ["Save expense"] =
                ("Enregistrer la dépense",
                 "حفظ المصروف"),
            ["Save movement"] =
                ("Enregistrer le mouvement",
                 "حفظ الحركة"),
            ["Save product"] =
                ("Enregistrer le produit",
                 "حفظ المنتج"),
            ["Saved. Restart the app to see it in the new language."] =
                ("Enregistré. Redémarrez l'application pour la voir dans la nouvelle langue.",
                 "تم الحفظ. أعد تشغيل التطبيق لرؤيته باللغة الجديدة."),
            ["Scan an item to see its price without selling it"] =
                ("Scannez un article pour voir son prix sans le vendre",
                 "امسح منتجاً لرؤية سعره دون بيعه"),
            ["Scan it"] =
                ("Scannez-le",
                 "امسحه ضوئياً"),
            ["Scan the next item, or press Esc to go back to selling"] =
                ("Scannez l'article suivant, ou appuyez sur Échap pour revenir à la vente",
                 "امسح المنتج التالي، أو اضغط Esc للعودة إلى البيع"),
            ["Scan the product"] =
                ("Scannez le produit",
                 "امسح المنتج"),
            ["Search"] =
                ("Rechercher",
                 "بحث"),
            ["See what was bought and what was paid"] =
                ("Voir ce qui a été acheté et payé",
                 "عرض ما اشتُري وما دُفع"),
            ["Sending…"] =
                ("Envoi…",
                 "جارٍ الإرسال…"),
            ["Set PIN"] =
                ("Définir le code",
                 "تعيين الرمز"),
            ["Set a smallest amount on a product and it will warn you here before it runs out."] =
                ("Fixez un minimum à un produit et il vous préviendra ici avant d'être épuisé.",
                 "حدد حداً أدنى لمنتج وسينبهك هنا قبل أن ينفد."),
            ["Set counted total"] =
                ("Saisir le total compté",
                 "إدخال المجموع المحسوب"),
            ["Set their password"] =
                ("Définir leur mot de passe",
                 "تعيين كلمة مرورهم"),
            ["Settings"] =
                ("Réglages",
                 "الإعدادات"),
            ["Shop settings"] =
                ("Réglages de la boutique",
                 "إعدادات المتجر"),
            ["Show hidden ones"] =
                ("Afficher les masqués",
                 "عرض المخفية"),
            ["Show past staff"] =
                ("Afficher les anciens employés",
                 "عرض الموظفين السابقين"),
            ["Shown after every amount in the app and on receipts"] =
                ("Affiché après chaque montant dans l'application et sur les tickets",
                 "يظهر بعد كل مبلغ في التطبيق وعلى الإيصالات"),
            ["Shown on the back office beside sales that came from here"] =
                ("Affiché dans l'arrière-boutique à côté des ventes venues d'ici",
                 "يظهر في الإدارة بجانب المبيعات القادمة من هنا"),
            ["Shown on the card instead of the icon. The icon is the fallback."] =
                ("Affichée sur la carte à la place de l'icône. L'icône reste le repli.",
                 "تظهر على البطاقة بدل الأيقونة. والأيقونة هي البديل."),
            ["Shown on the till, where the cashier presses it. Worth adding for anything without a barcode."] =
                ("Affichée en caisse, là où le caissier appuie. À ajouter pour tout ce qui n'a pas de code-barres.",
                 "تظهر في الصندوق حيث يضغط الكاشير. يستحسن إضافتها لكل ما لا يحمل باركود."),
            ["Sign in"] =
                ("Se connecter",
                 "تسجيل الدخول"),
            ["Sign out"] =
                ("Se déconnecter",
                 "تسجيل الخروج"),
            ["Sign {0} out"] =
                ("Déconnecter {0}",
                 "تسجيل خروج {0}"),
            ["Sold at the till"] =
                ("Vendu en caisse",
                 "يُباع في الصندوق"),
            ["Staff, wages and who can open the back office"] =
                ("Le personnel, les salaires et qui peut ouvrir l'arrière-boutique",
                 "الموظفون والأجور ومن يمكنه فتح الإدارة"),
            ["Stock that came in"] =
                ("Stock entré",
                 "المخزون الوارد"),
            ["Subtotal"] =
                ("Sous-total",
                 "المجموع الفرعي"),
            ["Supplier name or phone"] =
                ("Nom ou téléphone du fournisseur",
                 "اسم المورد أو هاتفه"),
            ["Supplier payment coming up."] =
                ("Paiement fournisseur à venir.",
                 "دفعة مورد قادمة."),
            ["Suppliers"] =
                ("Fournisseurs",
                 "الموردون"),
            ["TAKEN"] =
                ("ENCAISSÉ",
                 "المحصَّل"),
            ["TAKINGS"] =
                ("RECETTES",
                 "المداخيل"),
            ["TAKINGS · {0}"] =
                ("RECETTES · {0}",
                 "المداخيل · {0}"),
            ["THE LAST FORTNIGHT"] =
                ("LES QUINZE DERNIERS JOURS",
                 "الأسبوعان الأخيران"),
            ["THIS TILL IS CALLED"] =
                ("CETTE CAISSE S'APPELLE",
                 "اسم هذا الصندوق"),
            ["TOTAL"] =
                ("TOTAL",
                 "المجموع"),
            ["TOTAL COST"] =
                ("COÛT TOTAL",
                 "التكلفة الإجمالية"),
            ["Test"] =
                ("Tester",
                 "اختبار"),
            ["Test print"] =
                ("Test d'impression",
                 "طباعة تجريبية"),
            ["That barcode already belongs to another product."] =
                ("Ce code-barres appartient déjà à un autre produit.",
                 "هذا الباركود يخص منتجاً آخر بالفعل."),
            ["The last line of every receipt"] =
                ("La dernière ligne de chaque ticket",
                 "السطر الأخير في كل إيصال"),
            ["The minimum stock must be a number."] =
                ("Le stock minimum doit être un nombre.",
                 "يجب أن يكون الحد الأدنى للمخزون رقماً."),
            ["The new shelf price. Leave it as it is to keep the old one."] =
                ("Le nouveau prix de vente. Laissez tel quel pour garder l'ancien.",
                 "سعر الرف الجديد. اتركه كما هو للاحتفاظ بالقديم."),
            ["The photo is optional here — this product is scanned, so it only shows on lists and receipts."] =
                ("La photo est facultative ici — ce produit se scanne, elle n'apparaît donc que sur les listes et les tickets.",
                 "الصورة اختيارية هنا — هذا المنتج يُمسح ضوئياً، فتظهر على القوائم والإيصالات فقط."),
            ["The purchase price must be a number, like 6.20."] =
                ("Le prix d'achat doit être un nombre, comme 6.20.",
                 "يجب أن يكون سعر الشراء رقماً، مثل 6.20."),
            ["The receipt goes straight to the printer with no dialog. Turn this off to print only on demand from the Tickets page."] =
                ("Le ticket part directement à l'imprimante, sans fenêtre. Désactivez pour n'imprimer qu'à la demande depuis la page Tickets.",
                 "يذهب الإيصال مباشرة إلى الطابعة دون نافذة. أوقف هذا لتطبع عند الطلب فقط من صفحة التذاكر."),
            ["The selling price is below the cost — every sale of this product loses money."] =
                ("Le prix de vente est inférieur au coût — chaque vente de ce produit fait perdre de l'argent.",
                 "سعر البيع أقل من التكلفة — كل بيع لهذا المنتج يخسر مالاً."),
            ["The selling price must be a number, like 8.50."] =
                ("Le prix de vente doit être un nombre, comme 8.50.",
                 "يجب أن يكون سعر البيع رقماً، مثل 8.50."),
            ["The shop does not sell this yet. Add it in the back office and it will scan next time."] =
                ("La boutique ne vend pas encore cet article. Ajoutez-le dans l'arrière-boutique et il se scannera la prochaine fois.",
                 "المتجر لا يبيع هذا بعد. أضفه من الإدارة وسيُمسح في المرة القادمة."),
            ["The shop, and how it prints"] =
                ("La boutique, et comment elle imprime",
                 "المتجر، وطريقة الطباعة"),
            ["The stock quantity must be a number."] =
                ("La quantité en stock doit être un nombre.",
                 "يجب أن تكون كمية المخزون رقماً."),
            ["The till took"] =
                ("La caisse a encaissé",
                 "ما حصّله الصندوق"),
            ["The two PINs do not match."] =
                ("Les deux codes ne correspondent pas.",
                 "الرمزان غير متطابقين."),
            ["The two passwords do not match."] =
                ("Les deux mots de passe ne correspondent pas.",
                 "كلمتا المرور غير متطابقتين."),
            ["The whole app, in your language. It changes when the app restarts."] =
                ("Toute l'application, dans votre langue. Le changement prend effet au redémarrage.",
                 "التطبيق كله بلغتك. يتغير عند إعادة تشغيل التطبيق."),
            ["Their deliveries and payments show up here."] =
                ("Leurs livraisons et paiements apparaissent ici.",
                 "تظهر توصيلاتهم ومدفوعاتهم هنا."),
            ["Their details, role and wage"] =
                ("Leurs informations, rôle et salaire",
                 "بياناتهم ودورهم وأجرهم"),
            ["This month"] =
                ("Ce mois-ci",
                 "هذا الشهر"),
            ["This product has no barcode"] =
                ("Ce produit n'a pas de code-barres",
                 "هذا المنتج بدون باركود"),
            ["This week"] =
                ("Cette semaine",
                 "هذا الأسبوع"),
            ["This year"] =
                ("Cette année",
                 "هذه السنة"),
            ["Tick what is coming back, then say why."] =
                ("Cochez ce qui revient, puis indiquez pourquoi.",
                 "حدد ما يُرجع، ثم بيّن السبب."),
            ["Tickets"] =
                ("Tickets",
                 "التذاكر"),
            ["Today"] =
                ("Aujourd'hui",
                 "اليوم"),
            ["Total"] =
                ("Total",
                 "المجموع"),
            ["Try a different name or barcode."] =
                ("Essayez un autre nom ou code-barres.",
                 "جرّب اسماً أو باركود آخر."),
            ["Try a different name, or clear the filter."] =
                ("Essayez un autre nom, ou enlevez le filtre.",
                 "جرّب اسماً آخر، أو امسح المرشّح."),
            ["Try a different search, or set the cashier back to Any."] =
                ("Essayez une autre recherche, ou remettez le caissier sur Tous.",
                 "جرّب بحثاً آخر، أو أعد الكاشير إلى الكل."),
            ["Type a receipt number in the search bar above, or pick a ticket to view and reprint it."] =
                ("Saisissez un numéro de ticket dans la barre de recherche, ou choisissez un ticket pour le voir et le réimprimer.",
                 "اكتب رقم إيصال في شريط البحث أعلاه، أو اختر تذكرة لعرضها وإعادة طباعتها."),
            ["UNITS"] =
                ("UNITÉS",
                 "الوحدات"),
            ["Unlock"] =
                ("Déverrouiller",
                 "فتح القفل"),
            ["Use at least 4 characters, or leave both boxes empty to turn the password off."] =
                ("Utilisez au moins 4 caractères, ou laissez les deux champs vides pour désactiver le mot de passe.",
                 "استخدم 4 محارف على الأقل، أو اترك الحقلين فارغين لتعطيل كلمة المرور."),
            ["Use at least 4 characters."] =
                ("Utilisez au moins 4 caractères.",
                 "استخدم 4 محارف على الأقل."),
            ["Use code"] =
                ("Utiliser ce code",
                 "استخدام الرمز"),
            ["Uses the till and can see their own sales. Nothing else in the back office."] =
                ("Utilise la caisse et voit ses propres ventes. Rien d'autre dans l'arrière-boutique.",
                 "يستخدم الصندوق ويرى مبيعاته فقط. لا شيء آخر في الإدارة."),
            ["VAT"] =
                ("TVA",
                 "الضريبة"),
            ["View all {0}"] =
                ("Voir les {0}",
                 "عرض الكل ({0})"),
            ["WAGE"] =
                ("SALAIRE",
                 "الأجر"),
            ["WAGES DUE"] =
                ("SALAIRES DUS",
                 "الأجور المستحقة"),
            ["WHAT FOR"] =
                ("POUR QUOI",
                 "لماذا"),
            ["WHAT WAS IT FOR"] =
                ("C'ÉTAIT POUR QUOI",
                 "لأي غرض"),
            ["WHAT WE BUY"] =
                ("CE QUE NOUS ACHETONS",
                 "ما نشتريه"),
            ["WHEN"] =
                ("QUAND",
                 "متى"),
            ["WHERE IT GOES"] =
                ("OÙ ÇA VA",
                 "إلى أين يذهب"),
            ["WHERE THE MONEY WENT"] =
                ("OÙ EST PASSÉ L'ARGENT",
                 "أين ذهب المال"),
            ["WORKER"] =
                ("EMPLOYÉ",
                 "الموظف"),
            ["What did they bring?"] =
                ("Qu'ont-ils livré ?",
                 "ماذا أحضروا؟"),
            ["What it was for"] =
                ("À quoi ça servait",
                 "لأي غرض كان"),
            ["What the back office shows depends on who you are, and everything saved in it is recorded against you."] =
                ("Ce que montre l'arrière-boutique dépend de qui vous êtes, et tout ce qui y est enregistré l'est à votre nom.",
                 "ما تعرضه الإدارة يتوقف على هويتك، وكل ما يُحفظ فيها يُسجَّل باسمك."),
            ["What the shop holds, and what it cost"] =
                ("Ce que la boutique détient, et ce qu'il a coûté",
                 "ما يملكه المتجر، وكم كلّف"),
            ["What the shop made, and what needs doing"] =
                ("Ce que la boutique a gagné, et ce qui reste à faire",
                 "ما ربحه المتجر، وما ينبغي فعله"),
            ["What&#39;s selling"] =
                ("Ce qui se vend",
                 "ما الذي يُباع"),
            ["What's selling"] =
                ("Ce qui se vend",
                 "ما الذي يُباع"),
            ["Where the back-office computer answers, e.g. http://192.168.1.20:5000"] =
                ("Où répond l'ordinateur de l'arrière-boutique, ex. http://192.168.1.20:5000",
                 "حيث يستجيب جهاز الإدارة، مثلاً http://192.168.1.20:5000"),
            ["Who changed what, and every movement of stock"] =
                ("Qui a changé quoi, et chaque mouvement de stock",
                 "من غيّر ماذا، وكل حركة للمخزون"),
            ["Who the shop buys from, and what it still owes them"] =
                ("Chez qui la boutique achète, et ce qu'elle leur doit encore",
                 "ممن يشتري المتجر، وما زال مديناً به لهم"),
            ["Who, or what they touched"] =
                ("Qui, et ce qu'ils ont modifié",
                 "من، وما الذي عدّلوه"),
            ["Windows default is \"{0}\", which saves a file instead of printing. Receipts will NOT print automatically until a real receipt printer is selected above."] =
                ("L'imprimante Windows par défaut est « {0} », qui enregistre un fichier au lieu d'imprimer. Les tickets ne s'imprimeront PAS automatiquement tant qu'une vraie imprimante à tickets n'est pas choisie ci-dessus.",
                 "الطابعة الافتراضية في ويندوز هي «{0}»، وهي تحفظ ملفاً بدل الطباعة. لن تُطبع الإيصالات تلقائياً حتى تُختار طابعة إيصالات حقيقية أعلاه."),
            ["Windows default is currently: {0}"] =
                ("Imprimante Windows par défaut : {0}",
                 "الطابعة الافتراضية في ويندوز: {0}"),
            ["Windows reports no default printer on this machine."] =
                ("Windows ne signale aucune imprimante par défaut sur cette machine.",
                 "لا تُبلغ ويندوز عن أي طابعة افتراضية على هذا الجهاز."),
            ["Workers"] =
                ("Employés",
                 "العاملون"),
            ["Working offline"] =
                ("Hors ligne",
                 "يعمل دون اتصال"),
            ["Worth adding: with no barcode, this is what the cashier presses at the till."] =
                ("À ajouter : sans code-barres, c'est ce que le caissier presse en caisse.",
                 "يستحسن إضافتها: بلا باركود، هذا ما يضغطه الكاشير في الصندوق."),
            ["Wrong password."] =
                ("Mot de passe incorrect.",
                 "كلمة المرور خاطئة."),
            ["Yes"] =
                ("Oui",
                 "نعم"),
            ["Yesterday"] =
                ("Hier",
                 "أمس"),
            ["across {0} bill · {1}"] =
                ("sur {0} facture · {1}",
                 "على فاتورة واحدة · {1}"),
            ["across {0} bills · {1}"] =
                ("sur {0} factures · {1}",
                 "على {0} فواتير · {1}"),
            ["after the bills"] =
                ("après les factures",
                 "بعد الفواتير"),
            ["after {0} refunded"] =
                ("après {0} remboursés",
                 "بعد استرجاع {0}"),
            ["all with pictures"] =
                ("toutes avec une image",
                 "كلها بصور"),
            ["best {0}, {1}"] =
                ("meilleur {0}, {1}",
                 "الأفضل {0}، {1}"),
            ["bought &#183;"] =
                ("acheté &#183;",
                 "اشتُري &#183;"),
            ["bought ·"] =
                ("acheté ·",
                 "اشتُري ·"),
            ["broken, expired, lost or used in the shop, at what it cost"] =
                ("cassé, périmé, perdu ou utilisé dans la boutique, à son coût",
                 "مكسور أو منتهي الصلاحية أو ضائع أو مستعمل في المتجر، بتكلفته"),
            ["by units sold"] =
                ("par unités vendues",
                 "حسب الوحدات المباعة"),
            ["completed in this period"] =
                ("terminées sur cette période",
                 "مكتملة في هذه الفترة"),
            ["cost you {0} · you keep {1} ({2}%)"] =
                ("vous a coûté {0} · vous gardez {1} ({2} %)",
                 "كلّفك {0} · تحتفظ بـ {1} ({2}%)"),
            ["everyone is paid up"] =
                ("tout le monde est payé",
                 "الجميع مدفوع لهم"),
            ["everything is grouped"] =
                ("tout est regroupé",
                 "كل شيء مجمَّع"),
            ["everything stocked"] =
                ("tout est en stock",
                 "كل شيء متوفر"),
            ["filtered by {0}"] =
                ("filtré par {0}",
                 "مرشَّح حسب {0}"),
            ["for {0}"] =
                ("pour {0}",
                 "لـ {0}"),
            ["goods received. Not an expense: the shop swapped money for stock and is no poorer until it sells."] =
                ("marchandises reçues. Pas une dépense : la boutique a échangé de l'argent contre du stock et n'est pas plus pauvre tant qu'il ne se vend pas.",
                 "بضاعة مستلمة. ليست مصروفاً: بادل المتجر مالاً بمخزون ولا يصير أفقر حتى يبيعه."),
            ["includes {0} DH of wages"] =
                ("dont {0} DH de salaires",
                 "منها {0} درهم أجور"),
            ["items sold"] =
                ("articles vendus",
                 "منتجات مباعة"),
            ["last {0}"] =
                ("dernier {0}",
                 "آخر مرة {0}"),
            ["lines on the receipts"] =
                ("lignes sur les tickets",
                 "أسطر على الإيصالات"),
            ["more than this period's wages"] =
                ("plus que les salaires de la période",
                 "أكثر من أجور هذه الفترة"),
            ["no cost recorded yet"] =
                ("aucun coût enregistré",
                 "لم تُسجَّل تكلفة بعد"),
            ["no monthly bills marked yet"] =
                ("aucune facture mensuelle marquée",
                 "لم تُحدَّد فواتير شهرية بعد"),
            ["no products yet"] =
                ("aucun produit",
                 "لا منتجات بعد"),
            ["no sales in this period"] =
                ("aucune vente sur cette période",
                 "لا مبيعات في هذه الفترة"),
            ["no sales to measure"] =
                ("aucune vente à mesurer",
                 "لا مبيعات للقياس"),
            ["no wages agreed yet"] =
                ("aucun salaire convenu",
                 "لم يُتفق على أجر بعد"),
            ["nobody added yet"] =
                ("personne d'ajouté",
                 "لم يُضف أحد بعد"),
            ["none added yet"] =
                ("aucun ajouté",
                 "لم يُضف أحد بعد"),
            ["none left on the shelf"] =
                ("plus rien en rayon",
                 "لم يبق شيء على الرف"),
            ["none yet"] =
                ("aucune",
                 "لا شيء بعد"),
            ["nothing left the shelf"] =
                ("rien n'a quitté le rayon",
                 "لم يخرج شيء من الرف"),
            ["nothing outstanding"] =
                ("rien en attente",
                 "لا شيء مستحق"),
            ["nothing paid yet"] =
                ("rien de payé pour le moment",
                 "لم يُدفع شيء بعد"),
            ["nothing recorded"] =
                ("rien d'enregistré",
                 "لم يُسجَّل شيء"),
            ["nothing sold"] =
                ("rien de vendu",
                 "لم يُبع شيء"),
            ["nothing sold yet"] =
                ("rien de vendu pour l'instant",
                 "لم يُبع شيء بعد"),
            ["nothing spent in this period"] =
                ("rien de dépensé sur cette période",
                 "لا مصاريف في هذه الفترة"),
            ["of {0} product"] =
                ("sur {0} produit",
                 "من أصل {0} منتج"),
            ["of {0} products"] =
                ("sur {0} produits",
                 "من أصل {0} منتجات"),
            ["of {0} taken"] =
                ("sur {0} encaissés",
                 "من {0} محصَّلة"),
            ["on the books"] =
                ("dans les registres",
                 "في السجلات"),
            ["only findable by barcode or name"] =
                ("trouvables seulement par code-barres ou par nom",
                 "لا يمكن إيجادها إلا بالباركود أو الاسم"),
            ["purchase prices missing"] =
                ("prix d'achat manquants",
                 "أسعار الشراء ناقصة"),
            ["rent, light, water, internet and the rest"] =
                ("loyer, électricité, eau, internet et le reste",
                 "الكراء والكهرباء والماء والإنترنت وما تبقى"),
            ["rent, power, water, wifi"] =
                ("loyer, électricité, eau, wifi",
                 "الكراء والكهرباء والماء والواي فاي"),
            ["running low"] =
                ("bientôt épuisé",
                 "على وشك النفاد"),
            ["settled"] =
                ("soldé",
                 "مسدَّد"),
            ["stock received, all time"] =
                ("stock reçu, depuis toujours",
                 "المخزون المستلم، منذ البداية"),
            ["suppliers paid, bills and wages. Stock bought on credit is not here — only what was handed over."] =
                ("fournisseurs payés, factures et salaires. Le stock acheté à crédit n'est pas ici — seulement ce qui a été remis.",
                 "الموردون المدفوع لهم والفواتير والأجور. المخزون المشترى بالدين ليس هنا — فقط ما سُلِّم فعلاً."),
            ["taken over the counter"] =
                ("encaissé au comptoir",
                 "محصَّل على المنضدة"),
            ["the bills came to more than the {0} taken"] =
                ("les factures ont dépassé les {0} encaissés",
                 "تجاوزت الفواتير {0} المحصَّلة"),
            ["the price paid for exactly what was sold, frozen at the moment of sale"] =
                ("le prix payé pour exactement ce qui a été vendu, figé au moment de la vente",
                 "الثمن المدفوع لما بيع بالضبط، مثبَّتاً لحظة البيع"),
            ["the shop spent more than it made in this period"] =
                ("la boutique a dépensé plus qu'elle n'a gagné sur cette période",
                 "أنفق المتجر أكثر مما ربح في هذه الفترة"),
            ["these {0} brought in {1} of the {2} taken"] =
                ("ces {0} ont rapporté {1} sur les {2} encaissés",
                 "هذه {0} حققت {1} من أصل {2} محصَّلة"),
            ["to"] =
                ("à",
                 "إلى"),
            ["to {0} supplier"] =
                ("à {0} fournisseur",
                 "لـ {0} مورد"),
            ["to {0} suppliers"] =
                ("à {0} fournisseurs",
                 "لـ {0} موردين"),
            ["to {0} worker"] =
                ("à {0} employé",
                 "لـ {0} موظف"),
            ["to {0} workers"] =
                ("à {0} employés",
                 "لـ {0} موظفين"),
            ["what actually went to staff in this period"] =
                ("ce qui est réellement allé au personnel sur cette période",
                 "ما ذهب فعلاً إلى الموظفين في هذه الفترة"),
            ["what the shop actually kept"] =
                ("ce que la boutique a réellement gardé",
                 "ما احتفظ به المتجر فعلاً"),
            ["what the stock in them cost"] =
                ("ce que leur stock a coûté",
                 "كم كلّف المخزون فيها"),
            ["working here"] =
                ("travaillent ici",
                 "يعملون هنا"),
            ["{0} ({1} left)"] =
                ("{0} ({1} restants)",
                 "{0} (بقي {1})"),
            ["{0} average sale"] =
                ("{0} par vente en moyenne",
                 "{0} متوسط البيع"),
            ["{0} bill that comes back"] =
                ("{0} facture qui revient",
                 "فاتورة واحدة تتكرر"),
            ["{0} bills that come back"] =
                ("{0} factures qui reviennent",
                 "{0} فواتير تتكرر"),
            ["{0} can open the back office · each sees only the pages their role allows"] =
                ("{0} peuvent ouvrir l'arrière-boutique · chacun ne voit que les pages permises par son rôle",
                 "{0} يمكنهم فتح الإدارة · كل واحد يرى الصفحات التي يسمح بها دوره"),
            ["{0} changes · {1} stock movements · {2}"] =
                ("{0} modifications · {1} mouvements de stock · {2}",
                 "{0} تغييرات · {1} حركات مخزون · {2}"),
            ["{0} each"] =
                ("{0} l'unité",
                 "{0} للوحدة"),
            ["{0} hidden from the till"] =
                ("{0} masquées en caisse",
                 "{0} مخفية عن الصندوق"),
            ["{0} is not in the shop. Add it in the back office."] =
                ("{0} n'est pas dans la boutique. Ajoutez-le dans l'arrière-boutique.",
                 "{0} غير موجود في المتجر. أضفه من الإدارة."),
            ["{0} is not sold at the till."] =
                ("{0} n'est pas vendu en caisse.",
                 "{0} لا يُباع في الصندوق."),
            ["{0} items · {1} a basket"] =
                ("{0} articles · {1} par panier",
                 "{0} منتجات · {1} للسلة"),
            ["{0} kind of bill, {1} in all."] =
                ("{0} type de facture, {1} en tout.",
                 "نوع واحد من الفواتير، {1} إجمالاً."),
            ["{0} kinds of bill, {1} in all."] =
                ("{0} types de factures, {1} en tout.",
                 "{0} أنواع من الفواتير، {1} إجمالاً."),
            ["{0} matches — tap the one you want"] =
                ("{0} résultats — touchez celui que vous voulez",
                 "{0} نتائج — المس ما تريده"),
            ["{0} no longer here"] =
                ("{0} ne sont plus là",
                 "{0} لم يعودوا هنا"),
            ["{0} no longer used"] =
                ("{0} ne servent plus",
                 "{0} لم تعد مستعملة"),
            ["{0} of bills on {1} taken"] =
                ("{0} de factures pour {1} encaissés",
                 "{0} فواتير مقابل {1} محصَّلة"),
            ["{0} of them at zero"] =
                ("dont {0} à zéro",
                 "منها {0} عند الصفر"),
            ["{0} of {1} paid this month."] =
                ("{0} sur {1} payés ce mois-ci.",
                 "دُفع {0} من أصل {1} هذا الشهر."),
            ["{0} product"] =
                ("{0} produit",
                 "{0} منتج"),
            ["{0} product expires within {1} days"] =
                ("{0} produit périme sous {1} jours",
                 "{0} منتج تنتهي صلاحيته خلال {1} أيام"),
            ["{0} product has expired"] =
                ("{0} produit périmé",
                 "{0} منتج انتهت صلاحيته"),
            ["{0} product has no category. A cashier can only reach it by scanning or typing the name."] =
                ("{0} produit n'a pas de catégorie. Un caissier ne peut l'atteindre qu'en le scannant ou en tapant son nom.",
                 "{0} منتج بلا فئة. لا يمكن للكاشير الوصول إليه إلا بمسحه أو بكتابة اسمه."),
            ["{0} product is out of stock"] =
                ("{0} produit en rupture",
                 "{0} منتج نفد من المخزون"),
            ["{0} product is running low"] =
                ("{0} produit bientôt épuisé",
                 "{0} منتج على وشك النفاد"),
            ["{0} product sold, {1} in all"] =
                ("{0} produit vendu, {1} au total",
                 "{0} منتج مباع، {1} إجمالاً"),
            ["{0} product · {1} of stock"] =
                ("{0} produit · {1} de stock",
                 "{0} منتج · {1} من المخزون"),
            ["{0} product, newest first"] =
                ("{0} produit, le plus récent en premier",
                 "{0} منتج، الأحدث أولاً"),
            ["{0} products"] =
                ("{0} produits",
                 "{0} منتجات"),
            ["{0} products are out of stock"] =
                ("{0} produits en rupture",
                 "{0} منتجات نفدت من المخزون"),
            ["{0} products are running low"] =
                ("{0} produits bientôt épuisés",
                 "{0} منتجات على وشك النفاد"),
            ["{0} products expire within {1} days"] =
                ("{0} produits périment sous {1} jours",
                 "{0} منتجات تنتهي صلاحيتها خلال {1} أيام"),
            ["{0} products have expired"] =
                ("{0} produits périmés",
                 "{0} منتجات انتهت صلاحيتها"),
            ["{0} products have no category. A cashier can only reach them by scanning or typing the name."] =
                ("{0} produits n'ont pas de catégorie. Un caissier ne peut les atteindre qu'en les scannant ou en tapant leur nom.",
                 "{0} منتجات بلا فئة. لا يمكن للكاشير الوصول إليها إلا بمسحها أو بكتابة اسمها."),
            ["{0} products have no purchase price, so profit cannot be worked out yet. Add cost prices under Inventory."] =
                ("{0} produits n'ont pas de prix d'achat, le bénéfice ne peut donc pas encore être calculé. Ajoutez les coûts sous Stock.",
                 "{0} منتجات بلا سعر شراء، لذا لا يمكن حساب الربح بعد. أضف أسعار التكلفة تحت المخزون."),
            ["{0} products match “{1}” — scan it, or type more of the name"] =
                ("{0} produits correspondent à « {1} » — scannez-le, ou tapez plus du nom",
                 "{0} منتجات تطابق «{1}» — امسحه ضوئياً أو اكتب المزيد من الاسم"),
            ["{0} products sold, {1} in all"] =
                ("{0} produits vendus, {1} au total",
                 "{0} منتجات مباعة، {1} إجمالاً"),
            ["{0} products · {1} of stock"] =
                ("{0} produits · {1} de stock",
                 "{0} منتجات · {1} من المخزون"),
            ["{0} products, newest first"] =
                ("{0} produits, les plus récents en premier",
                 "{0} منتجات، الأحدث أولاً"),
            ["{0} sale affected"] =
                ("{0} vente concernée",
                 "{0} عملية متأثرة"),
            ["{0} sale · {1} DH a basket"] =
                ("{0} vente · {1} DH par panier",
                 "{0} عملية · {1} درهم للسلة"),
            ["{0} sales affected"] =
                ("{0} ventes concernées",
                 "{0} عمليات متأثرة"),
            ["{0} sales · {1} DH a basket"] =
                ("{0} ventes · {1} DH par panier",
                 "{0} عمليات · {1} درهم للسلة"),
            ["{0} sales, after {1} of discounts and {2} refunded"] =
                ("{0} ventes, après {1} de remises et {2} remboursés",
                 "{0} عمليات بيع، بعد {1} تخفيضات و{2} مسترجعة"),
            ["{0} sales, after {1} refunded"] =
                ("{0} ventes, après {1} remboursés",
                 "{0} عمليات بيع، بعد استرجاع {1}"),
            ["{0} still owed of {1} bought."] =
                ("{0} encore dus sur {1} achetés.",
                 "ما زال {0} مستحقاً من أصل {1} مشتراة."),
            ["{0} to look at, {1} urgent."] =
                ("{0} à regarder, {1} urgents.",
                 "{0} للمراجعة، {1} عاجلة."),
            ["{0} to look at."] =
                ("{0} à regarder.",
                 "{0} للمراجعة."),
            ["{0} unpaid on this delivery"] =
                ("{0} impayés sur cette livraison",
                 "{0} غير مدفوعة على هذا التوصيل"),
            ["{0} with a picture"] =
                ("{0} avec une image",
                 "{0} بصورة"),
            ["{0} with no cost recorded"] =
                ("{0} sans coût enregistré",
                 "{0} بلا تكلفة مسجلة"),
            ["{0} · {1}% of the total"] =
                ("{0} · {1} % du total",
                 "{0} · {1}% من المجموع"),
            ["{0} × {1} at {2}"] =
                ("{0} × {1} à {2}",
                 "{0} × {1} بـ {2}"),
            ["{0} — {1} due {2}"] =
                ("{0} — {1} à payer {2}",
                 "{0} — {1} مستحقة {2}"),
            ["{0} — {1} overdue"] =
                ("{0} — {1} en retard",
                 "{0} — {1} متأخرة"),
            ["{0} — {1} unpaid"] =
                ("{0} — {1} impayés",
                 "{0} — {1} غير مدفوعة"),
            ["{0}% margin"] =
                ("{0} % de marge",
                 "هامش {0}%"),
            ["{0}% of what is due"] =
                ("{0} % de ce qui est dû",
                 "{0}% مما هو مستحق"),
            ["{0}% of what was bought"] =
                ("{0} % de ce qui a été acheté",
                 "{0}% مما تم شراؤه"),
            ["{0}% of what was taken"] =
                ("{0} % de ce qui a été encaissé",
                 "{0}% مما تم تحصيله"),
            ["{0}% of what was taken, before any bills"] =
                ("{0} % de ce qui a été encaissé, avant les factures",
                 "{0}% مما تم تحصيله، قبل أي فواتير"),
            ["{0}% of what you charged"] =
                ("{0} % de ce que vous avez facturé",
                 "{0}% مما طلبته"),
            ["{0}, DAY BY DAY"] =
                ("{0}, JOUR PAR JOUR",
                 "{0}، يوماً بيوم"),
            ["{0}, MONTH BY MONTH"] =
                ("{0}, MOIS PAR MOIS",
                 "{0}، شهراً بشهر"),
            ["− bills"] =
                ("− factures",
                 "− الفواتير"),
            ["− stock written off"] =
                ("− stock passé en perte",
                 "− المخزون المشطوب"),
            ["− wages paid"] =
                ("− salaires versés",
                 "− الأجور المدفوعة"),
            ["− what those goods cost the shop"] =
                ("− ce que ces marchandises ont coûté",
                 "− ما كلّفت تلك البضاعة المتجر"),
        };
}
