---
title: "付録 EF Core 2：モデル定義（キー・採番・SQL Server 固有）"
description: "EF Core の代替キーとインデックス、シャドウプロパティ、シーケンスと主キーの採番、テンポラルテーブル、空間データ、hierarchyid など SQL Server 固有のマッピングを解説します。第8章の付録です。"
---

このページは [第8章：データベースアクセスと ORM (Entity Framework Core)](../08-entity-framework-core/index.md) の付録です。キーとインデックス、採番と履歴、SQL Server 固有のマッピング、モデル全体にかかる構成を扱います。

本編を先に読んでから、必要な項目をここで参照してください。

**第8章のほかの付録**

- [付録 EF Core 1：モデル定義（エンティティとリレーションシップ）](../appendix-efcore-01/index.md)
- [付録 EF Core 3：マイグレーションの詳細とクエリ](../appendix-efcore-03/index.md)
- [付録 EF Core 4：更新・トランザクション・レプリカ](../appendix-efcore-04/index.md)
- [付録 EF Core 5：パフォーマンス](../appendix-efcore-05/index.md)
- [付録 EF Core 6：テスト](../appendix-efcore-06/index.md)


---

## 目次

1. [キーとインデックス](#1-キーとインデックス)
   - [代替キーと一意インデックス](#代替キーと一意インデックス)
   - [1 つのテーブルを複数のエンティティで共有する](#1-つのテーブルを複数のエンティティで共有する)
   - [キーなしエンティティ型でビューや集計結果を読む](#キーなしエンティティ型でビューや集計結果を読む)
   - [チェック制約で不正な値をデータベース側で弾く](#チェック制約で不正な値をデータベース側で弾く)
   - [シャドウプロパティとバッキングフィールド](#シャドウプロパティとバッキングフィールド)
2. [採番と履歴](#2-採番と履歴)
   - [シーケンスによる採番](#シーケンスによる採番)
   - [主キーの採番方法を細かく制御する](#主キーの採番方法を細かく制御する)
   - [監査履歴を自動で残す（テンポラルテーブル）](#監査履歴を自動で残すテンポラルテーブル)
3. [SQL Server 固有のマッピング](#3-sql-server-固有のマッピング)
   - [空間データ](#空間データ)
   - [hierarchyid で階層構造を扱う](#hierarchyid-で階層構造を扱う)
   - [SQL Server 固有の列オプション](#sql-server-固有の列オプション)
   - [計算列](#計算列)
   - [Azure SQL の価格レベルをマイグレーションで指定する](#azure-sql-の価格レベルをマイグレーションで指定する)
   - [SQL Server の互換性レベルを明示する](#sql-server-の互換性レベルを明示する)
4. [モデル全体にかかる構成](#4-モデル全体にかかる構成)
   - [コマンドのタイムアウト](#コマンドのタイムアウト)
   - [一括構成（規約前の構成）](#一括構成規約前の構成)
   - [グローバルクエリフィルターと名前付きクエリフィルター](#グローバルクエリフィルターと名前付きクエリフィルター)
   - [コード分析ルールがエンティティ定義と衝突する](#コード分析ルールがエンティティ定義と衝突する)
5. [参考ドキュメント](#5-参考ドキュメント)

---

## 1. キーとインデックス

### 代替キーと一意インデックス

主キー以外の列を「もう 1 つの一意な識別子」として扱いたい場合は **代替キー (alternate key)** を構成します。

```csharp
modelBuilder.Entity<User>().HasAlternateKey(u => u.Email);
```

次は、SQL Server での生成 DDL の確認例です。`AK_` で始まる名前の一意制約が含まれています。

```sql
[Email] nvarchar(450) NOT NULL,
CONSTRAINT [AK_Users_Email] UNIQUE ([Email])
```

代替キーは外部キーの参照先にできます。

```csharp
modelBuilder.Entity<Order>()
    .HasOne(o => o.User)
    .WithMany(u => u.Orders)
    .HasForeignKey(o => o.UserEmail)
    .HasPrincipalKey(u => u.Email);
```

SQL Server では、外部キーの参照先が主キーではなく代替キーになります。

```sql
CONSTRAINT [FK_Orders_Users_UserEmail] FOREIGN KEY ([UserEmail]) REFERENCES [Users] ([Email]) ON DELETE CASCADE
```

次は、参照先に存在しないメールアドレスで `Order` を保存する確認例のエラーです。

```text
The INSERT statement conflicted with the FOREIGN KEY constraint "FK_Orders_Users_UserEmail".
```

> [!TIP]
> **単に一意性を強制したいだけなら、代替キーではなく一意インデックス (`HasIndex(...).IsUnique()`) を使ってください。** 公式ドキュメントも同じ指針を示しています。代替キーが一意インデックスと違うのは、外部キーの参照先にできる点です。
>
> なお、代替キーは明示的に構成しなくても導入されることがあります。一意インデックスを持つプロパティを `HasPrincipalKey` の対象にする確認例でも、`AK_Users_Email` 制約と `IX_Users_Email` 一意インデックスの両方を確認できています。

#### 代替キーを結合テーブルの参照先にする

多対多の結合テーブルの外部キーは、既定では両側の主キーを参照します。これを代替キーに向けることもできます。`UsingEntity` に右側 (`r`) と左側 (`l`) の 2 つのラムダを渡し、それぞれで `HasPrincipalKey` を指定します。

```csharp
modelBuilder.Entity<Post>()
    .HasMany(e => e.Tags)
    .WithMany(e => e.Posts)
    .UsingEntity(
        r => r.HasOne(typeof(Tag)).WithMany().HasPrincipalKey(nameof(Tag.AlternateKey)),
        l => l.HasOne(typeof(Post)).WithMany().HasPrincipalKey(nameof(Post.AlternateKey)));
```

次は、SQL Server 2022 向けの生成 DDL の確認例です。`HasAlternateKey` を書いていないのに `AK_Posts_AlternateKey` が作られている点に注目してください。前述のとおり `HasPrincipalKey` が代替キーを自動で導入します。

```sql
CREATE TABLE [PostTag] (
    [PostsAlternateKey] int NOT NULL,
    [TagsAlternateKey] int NOT NULL,
    CONSTRAINT [PK_PostTag] PRIMARY KEY ([PostsAlternateKey], [TagsAlternateKey]),
    CONSTRAINT [FK_PostTag_Posts_PostsAlternateKey] FOREIGN KEY ([PostsAlternateKey]) REFERENCES [Posts] ([AlternateKey]) ON DELETE CASCADE,
    CONSTRAINT [FK_PostTag_Tag_TagsAlternateKey] FOREIGN KEY ([TagsAlternateKey]) REFERENCES [Tag] ([AlternateKey]) ON DELETE CASCADE
);
```

結合テーブルの列名も主キー由来の `PostsId` ではなく `PostsAlternateKey` になります。`Include` で読み込むと、結合も代替キーで行われます（SQL Server で実測）。

```sql
LEFT JOIN (
    SELECT [p0].[PostsAlternateKey], [p0].[TagsAlternateKey], [t].[Id], [t].[AlternateKey]
    FROM [PostTag] AS [p0]
    INNER JOIN [Tag] AS [t] ON [p0].[TagsAlternateKey] = [t].[AlternateKey]
) AS [s] ON [p].[AlternateKey] = [s].[PostsAlternateKey]
```

> [!NOTE]
> この例では、[公式サンプル](https://learn.microsoft.com/ja-jp/ef/core/modeling/relationships/many-to-many#many-to-many-with-alternate-keys)と同じく、左右の関係をそれぞれ構成する 2 引数の `UsingEntity` オーバーロードを使います。各外部キーの参照先は `HasPrincipalKey` で指定します。

### 1 つのテーブルを複数のエンティティで共有する

大きな列を含むテーブルを扱うとき、「一覧では軽い列だけ読みたい」という要求があります。EF Core は 2 つのエンティティ型を同じテーブルにマップできます。これを **テーブル分割 (table splitting)** と呼びます。

```csharp
modelBuilder.Entity<Order>(b =>
{
    b.ToTable("Orders");
    b.Property(o => o.Status).HasColumnName("Status");
});
modelBuilder.Entity<DetailedOrder>(b =>
{
    b.ToTable("Orders");
    b.Property(o => o.Status).HasColumnName("Status");
});
modelBuilder.Entity<Order>()
    .HasOne(o => o.DetailedOrder)
    .WithOne()
    .HasForeignKey<DetailedOrder>(o => o.Id);
```

テーブルは 1 つだけ作られ、それぞれのエンティティは自分がマップされた列だけを読みます。SQL Server で実測したクエリは次のとおりです。

```sql
-- context.Orders
SELECT [o].[Id], [o].[Status] FROM [Orders] AS [o]

-- context.Detailed
SELECT [o].[Id], [o].[BillingAddress], [o].[ShippingAddress], [o].[Status]
FROM [Orders] AS [o]
WHERE [o].[BillingAddress] IS NOT NULL OR [o].[ShippingAddress] IS NOT NULL
```

> [!WARNING]
> 依存側のクエリに `IS NOT NULL` の条件が自動的に付いている点に注目してください。公式ドキュメントによると、**依存エンティティが使う列がすべて `NULL` の場合、EF Core はそのインスタンスを作りません。** このモデルで `Order` だけの行を追加した確認例でも、`Orders` は 2 件、`Detailed` は 1 件です。
>
> これは任意の依存エンティティを表現するための仕様ですが、公式は「依存側のプロパティがすべて省略可能でたまたま全部 `null` になった場合にも同じことが起きる。これは期待した動作ではないかもしれない」と注意しています。加えて、この判定はクエリ性能にも影響します。避けたい場合は依存エンティティを必須として構成してください。

1 つのエンティティを複数のテーブルに分けることもできます。こちらは **エンティティ分割 (entity splitting)** です。

```csharp
modelBuilder.Entity<Customer>(b =>
{
    b.ToTable("Customers");
    b.SplitToTable("CustomerAddresses", t =>
    {
        t.Property(c => c.Street);
        t.Property(c => c.City);
    });
});
```

このモデルの SQL Server での確認結果では、テーブルは 2 つ、クエリは `INNER JOIN` で、1 件の保存による影響行数は 2 です。

```sql
SELECT TOP(@p) [c].[Id], [c0].[City], [c].[Name], [c0].[Street]
FROM [Customers] AS [c]
INNER JOIN [CustomerAddresses] AS [c0] ON [c].[Id] = [c0].[Id]
```

> [!NOTE]
> 公式ドキュメントはエンティティ分割の制限として、**継承階層にある型では使えないこと**と、**主テーブルの行に対して分割先テーブルの行が必ず存在しなければならないこと**（分割された部分は省略できない）を挙げています。`INNER JOIN` になるのはこのためです。

### キーなしエンティティ型でビューや集計結果を読む

主キーを持たないデータ、たとえばデータベースビューや集計結果を読むための仕組みが **キーなしエンティティ型 (keyless entity type)** です。`HasNoKey()` または `[Keyless]` 属性で構成します。

```csharp
public class BlogPostCount
{
    public string Name { get; set; } = "";
    public int PostCount { get; set; }
}

protected override void OnModelCreating(ModelBuilder modelBuilder)
    => modelBuilder.Entity<BlogPostCount>()
        .HasNoKey()
        .ToView("View_BlogPostCounts");
```

`DbSet<BlogPostCount>` を定義すれば、通常のエンティティと同じように LINQ で問い合わせられます。SQL Server で実測したクエリと結果は次のとおりです。

```sql
SELECT [v].[Name], [v].[PostCount] FROM [View_BlogPostCounts] AS [v]
```

[公式のキーなしエンティティの説明](https://learn.microsoft.com/ja-jp/ef/core/modeling/keyless-entity-types#keyless-entity-types-characteristics)では、変更追跡の対象にならず、挿入・更新・削除にも使われません。上の確認例でもクエリ後の追跡エントリー数は 0 です。次は、`Add` を試みる場合の例外の確認例です。

```text
Unable to track an instance of type 'BlogPostCount' because it does not have a primary key.
Only entity types with a primary key may be tracked.
```

> [!NOTE]
> `ToTable` と `ToView` の違いは、EF Core から見て **読み取り専用として扱うかどうか** です。公式ドキュメントによると、`ToView` で指定したデータベースオブジェクトは読み取り専用のクエリソースとして扱われ、更新・挿入・削除の対象になりません。実際のデータベースオブジェクトがビューである必要はなく、主キーを持たないテーブルを読み取り専用として扱う用途にも使えます。

公式ドキュメントが挙げている主な用途は次の 4 つです。

- 生 SQL クエリの戻り値の型として使う
- 主キーを持たないデータベースビューにマップする
- 主キーが定義されていないテーブルにマップする
- モデル内で定義したクエリにマップする

> [!WARNING]
> キーなしエンティティ型には、通常のエンティティ型にはない制限があります。公式ドキュメントが挙げているもののうち、実務で引っかかりやすいのは次の点です。
>
> - **規約によって検出されることがない。** 必ず `HasNoKey()` か `[Keyless]` で明示的に構成する必要があります
> - **通常のエンティティ型からキーなしエンティティ型へのナビゲーションプロパティを持てない**
> - リレーションシップの主体側になれない
> - 継承階層は作れるが、TPH としてしかマップできない（継承の 3 つの方式は[付録1の「継承のマッピング」](../appendix-efcore-01/index.md#継承のマッピング)を参照）
> - テーブル分割・エンティティ分割は使えない

### チェック制約で不正な値をデータベース側で弾く

チェック制約 (Check Constraint) は、テーブルのすべての行が満たすべき条件を SQL 式で定義するリレーショナルデータベースの標準機能です。NOT NULL 制約や一意制約と似ていますが、任意の SQL 式を書ける点が違います。EF Core では `ToTable` の中で `HasCheckConstraint` を使って構成します。

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Product>()
        .ToTable(t => t.HasCheckConstraint("CK_Product_Price", "[Price] > 0"));
}
```

生成される DDL（SQL Server）にはテーブル定義の一部として制約が含まれます。

```sql
CREATE TABLE [Products] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NOT NULL,
    [Price] decimal(18,2) NOT NULL,
    CONSTRAINT [PK_Products] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Product_Price] CHECK ([Price] > 0)
);
```

同じテーブルに複数のチェック制約を、それぞれ別の名前で定義できます。

> [!WARNING]
> **チェック制約はデータベースが強制する制約です。** 次は、このモデルで `Price = -1` を保存する確認例の `DbUpdateException` の内部例外です。EF Core による保存前の入力検証ではありません。
>
> ```text
> SqlException: The INSERT statement conflicted with the CHECK constraint "CK_Product_Price".
> The conflict occurred in database "CcTest", table "dbo.Products", column 'Price'.
> ```
>
> チェック制約は「アプリケーションのバグや別経路からの書き込みでも不正なデータが入らない」という最後の砦です。利用者に見せるバリデーションは、アプリケーション側でも別途実装してください。

> [!NOTE]
> 「保存前に検証しない」は、任意の数値をそのまま保存できるという意味ではありません。[SqlClient 6.1.6 の公式実装](https://github.com/dotnet/SqlClient/blob/v6.1.6/src/Microsoft.Data.SqlClient/netcore/src/Microsoft/Data/SqlClient/TdsParser.cs#L9737-L9777)は、decimal パラメーターのスケールを調整してから精度を検査します。元の入力値の小数桁数の検証と、保存される値に対するチェック制約は区別してください。上の `decimal(18,2)` 列を使う Azure SQL / EF Core 10.0.11 / Microsoft.Data.SqlClient 6.1.6 の確認例では、`Price = 0.004m` はチェック制約違反、`Price = 0.005m` の保存値は `0.01` です。
>
> 桁数の上限も別です。[SQL Server の decimal の定義](https://learn.microsoft.com/ja-jp/sql/t-sql/data-types/decimal-and-numeric-transact-sql?view=sql-server-ver17)では、整数部の最大桁数は精度からスケールを引いた値で、`decimal(18,2)` では 16 桁です。同じ確認例では `9999999999999999.99` は保存成功、`10000000000000000` は失敗で、後者の内部例外は SqlClient の値域検査による `ArgumentException` です。

> [!TIP]
> よく使われるチェック制約の一部は、コミュニティパッケージの [EFCore.CheckConstraints](https://github.com/efcore/EFCore.CheckConstraints) で構成できます。EF Core の公式[チェック制約の説明](https://learn.microsoft.com/ja-jp/ef/core/modeling/indexes#check-constraints)も、このパッケージを紹介しています。

### シャドウプロパティとバッキングフィールド

エンティティクラスに書きたくない情報や、外部に公開したくない情報をデータベースに持たせたい場面があります。EF Core はそのための仕組みを 2 つ用意しています。

#### シャドウプロパティ

**シャドウプロパティ (Shadow Property)** は、C# のエンティティクラスには存在せず、EF Core のモデルにだけ存在するプロパティです。公式ドキュメントは「値と状態は変更追跡機能の中だけで保持される」と説明しています。監査用の更新日時のように、ドメインモデルには出したくないがテーブルには持たせたい列に向いています。

```csharp
modelBuilder.Entity<Blog>()
    .Property<DateTime>("LastUpdated");
```

生成される DDL には、通常のプロパティと同じように列が現れます（SQLite で実測）。

```sql
CREATE TABLE "Blogs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Blogs" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "LastUpdated" TEXT NOT NULL
);
```

C# のクラスにプロパティがないため、読み書きは `ChangeTracker` 経由で行います。クエリでは `EF.Property<T>` を使います。

```csharp
// 書き込み
context.Entry(blog).Property("LastUpdated").CurrentValue = DateTime.UtcNow;

// クエリ
var recent = await context.Blogs
    .Where(b => EF.Property<DateTime>(b, "LastUpdated") > threshold)
    .ToListAsync(cancellationToken);
```

> [!TIP]
> 実は、**外部キーを明示的に書かなかった場合の外部キーもシャドウプロパティ**です。公式ドキュメントは「シャドウプロパティは外部キーに最もよく使われる」と述べています。[本編の定義](../08-entity-framework-core/index.md#エンティティクラスの定義)の `Post.BlogId` を省き、`Blog` 側の `List<Post> Posts` だけで関係を表す確認例では、`Posts` に `BlogId` 列・外部キー制約・インデックスが生成され、`IsShadowProperty() == true` を確認できています。
>
> ```sql
> CREATE TABLE "Posts" (
>     "Id" INTEGER NOT NULL CONSTRAINT "PK_Posts" PRIMARY KEY AUTOINCREMENT,
>     "Title" TEXT NOT NULL,
>     "BlogId" INTEGER NULL,
>     CONSTRAINT "FK_Posts_Blogs_BlogId" FOREIGN KEY ("BlogId") REFERENCES "Blogs" ("Id")
> );
> ```
>
> ここで注目したいのは `BlogId` が **NULL 許容**になっている点です。公式ドキュメントは「規約では、シャドウ外部キーは関連する主キーから型を受け継ぎ、**リレーションシップが必須と判定または構成されない限り NULL 許容になる**」と説明しています。投稿が必ずブログに属するのであれば、`BlogId` を明示的に定義するか、`IsRequired()` で必須に構成してください。
>
> なお公式ドキュメントは、シャドウ外部キーを使う動機を「外部キーというリレーショナルな概念を、業務ロジックが使うドメインモデルから隠したい場合」と位置づけています。一方で「エンティティをシリアル化して送信する場合は、外部キーの値がリレーションシップの情報を保つのに役立つ」ため、外部キープロパティを `private` にして型の中には残す、という折衷案も紹介されています。

#### バッキングフィールド

**バッキングフィールド (Backing Field)** は、プロパティではなくフィールドを直接読み書きするように EF Core を構成する機能です。公式ドキュメントは「クラス側のカプセル化でアクセスを制限・拡張している場合に、その制限を経由せずにデータベースと読み書きしたいときに有用」と説明しています。

```csharp
public class Product
{
    private decimal _price;

    public int Id { get; set; }
    public decimal Price => _price;          // 読み取り専用

    public void SetPrice(decimal value)      // アプリケーションからの変更用
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        _price = value;
    }
}
```

```csharp
modelBuilder.Entity<Product>()
    .Property(p => p.Price)
    .HasField("_price");
```

この構成では、読込時に `SetPrice` を経由せず `_price` に書き込まれます。1 件を再読込する確認例でも、`SetPrice` の呼出回数は保存前の 1 回のままです。このメソッド内の検証は、アプリケーションからメソッドを呼ぶ変更に適用され、EF Core によるフィールドへの復元時には実行されません。

> [!WARNING]
> **この読み取り専用の `Price` を永続化するには、`Property(p => p.Price)` でモデルに含めます。** [公式ドキュメント](https://learn.microsoft.com/ja-jp/ef/core/modeling/backing-field#basic-configuration)が説明するように、バッキングフィールドの規約による発見は、モデルに含まれるプロパティが対象です。上の構成から `HasField` だけを省略しても、`Property` が残っていれば `_price` は命名規約で発見されます。`HasField` はフィールドを明示的に指定する設定であり、このフィールド名では必須ではありません。
>
> 次は、EF Core 10.0.11 / SQLite で `SetPrice(123m)` の後に保存・再読込する確認例です。
>
> | `Price` の構成 | `Price` 列 | 再読取した値 |
> | --- | --- | --- |
> | 構成なし | 作られない | `0` |
> | `Property(p => p.Price)` のみ | 作られる | `123` |
> | `Property(p => p.Price).HasField("_price")` | 作られる | `123` |
>
> この確認例の `SetPrice` の呼出回数は、いずれも最初の 1 回のままです。構成全体を省略した場合と、`HasField` だけを省略した場合を混同しないでください。

#### プロパティとフィールドのどちらを使うかを指定する

`HasField` を構成すると、公式ドキュメントによれば「EF は常にバッキングフィールドを読み書きし、プロパティを使うことはない」のが既定の動作です。この動作は `UsePropertyAccessMode` で変更できます。

```csharp
modelBuilder.Entity<Product>()
    .Property(p => p.Price)
    .HasField("_price")
    .UsePropertyAccessMode(PropertyAccessMode.Field);
```

指定できる値の一覧は `PropertyAccessMode` 列挙型を参照してください。たとえば「マテリアライズ（データベースから復元）するときだけフィールドに書き込み、それ以外はプロパティを使う」といった構成が可能です。

#### フィールドのみのプロパティ

CLR プロパティを一切持たず、フィールドだけでデータを保持する「フィールドのみのプロパティ」も定義できます。公式ドキュメントは、エンティティがプロパティではなくメソッドで値を出し入れする場合や、主キーのようにドメインモデルへ一切公開したくない場合の用途を挙げています。

```csharp
modelBuilder.Entity<Article>().Property("_validatedUrl");
```

これは変更追跡側にデータを持つシャドウプロパティとは異なり、**エンティティ側のフィールドにデータを持ちます。** LINQ から参照するには `EF.Property` を使います。SQL Server で実測したクエリは次のとおりです。

```csharp
var sorted = db.Articles.OrderBy(x => EF.Property<string>(x, "_validatedUrl"));
```

```sql
SELECT [a].[Id], [a].[Url], [a].[_validatedUrl] FROM [Articles] AS [a] ORDER BY [a].[_validatedUrl]
```

> [!NOTE]
> 公式ドキュメントによると、`Property("名前")` に指定した名前は **CLR プロパティ → フィールド** の順で探され、どちらも見つからない場合はシャドウプロパティとして構成されます。名前を打ち間違えても例外にならず、意図せずシャドウプロパティが増えるだけなので注意してください。

## 2. 採番と履歴

### シーケンスによる採番

`IDENTITY` はテーブルごとに独立した採番です。**複数のテーブルで連番を共有したい**場合は **シーケンス (Sequence)** を使います。公式ドキュメントは「シーケンスは特定のテーブルに結び付いておらず、複数のテーブルが同じシーケンスから値を引くように構成できる」と説明しています。

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.HasSequence<int>("DocumentNumbers")
        .StartsAt(1000)
        .IncrementsBy(5);

    modelBuilder.Entity<Order>()
        .Property(o => o.OrderNo)
        .HasDefaultValueSql("NEXT VALUE FOR DocumentNumbers");

    modelBuilder.Entity<Invoice>()
        .Property(i => i.DocNo)
        .HasDefaultValueSql("NEXT VALUE FOR DocumentNumbers");
}
```

次は、SQL Server 2022 向けの生成 DDL の確認例です。

```sql
CREATE SEQUENCE [DocumentNumbers] AS int START WITH 1000 INCREMENT BY 5 NO CYCLE;

CREATE TABLE [Orders] (
    [Id] int NOT NULL IDENTITY,
    [OrderNo] int NOT NULL DEFAULT (NEXT VALUE FOR DocumentNumbers),
    CONSTRAINT [PK_Orders] PRIMARY KEY ([Id])
);
```

主キーの `Id` は従来どおり `IDENTITY` で、`OrderNo` だけがシーケンスから採番されます。`Invoice` を 1 件、`Order` を 3 件保存した実測結果は次のとおりで、**2 つのテーブルにまたがって連番が振られている**ことが確認できます。

| テーブル | Id | 採番された番号 |
| --- | --- | --- |
| Invoice | 1 | 1000 |
| Order | 1 | 1005 |
| Order | 2 | 1010 |
| Order | 3 | 1015 |

> [!WARNING]
> `NEXT VALUE FOR` は SQL Server の構文です。公式ドキュメントも「シーケンスから値を生成する SQL はデータベース固有であり、上の例は SQL Server では動くが他のデータベースでは失敗する」と明記しています。PostgreSQL では `nextval('...')` のように書き換える必要があり、SQLite にはシーケンス自体がありません。

#### HiLo では保存前に採番の問い合わせが発生する

SQL Server の `UseHiLo` は、シーケンスから値のブロックを取得し、その範囲内でキーを割り当てます。使い切ったら次のブロックを要求します。通常の `Add` は追跡を始めるだけですが、公式の[Add と AddAsync の違い](https://learn.microsoft.com/ja-jp/ef/core/change-tracking/miscellaneous#add-versus-addasync)は、HiLo の採番をデータベースアクセスが発生し得る例外として挙げています。HiLo は既定の採番方式ではありません。

```csharp
// OnModelCreating 内。SQL Server の検証用モデルの採番を構成する
modelBuilder.UseHiLo("AuditHiLo");
```

次は、EF Core 10.0.11 / SQL Server 2022 で増分 10 のシーケンスを使い、新しいデータベースごとに 21 件を追加する確認例です。`SaveChangesAsync()` より前の `SELECT NEXT VALUE FOR [AuditHiLo]` の累計を示します。

| 追加 API | 10 件追加後 | 11 件追加後 | 21 件追加後 | 採番 SQL の実行経路 |
| --- | ---: | ---: | ---: | --- |
| `Add` | 1 回 | 2 回 | 3 回 | 同期 |
| `AddAsync` | 1 回 | 2 回 | 3 回 | 非同期 |
| `AddRangeAsync` | 1 回 | 2 回 | 3 回 | 非同期 |

この確認例の行数は、保存前は 0、`SaveChangesAsync()` 後は 21 です。**エンティティの INSERT がまだ行われていないことと、データベースへ一度もアクセスしていないことは別です。** この結果はブロックの補充の確認であり、並列実行や再起動後の欠番なしを保証するものではありません。

### 主キーの採番方法を細かく制御する

整数の主キーは、規約により SQL Server では `IDENTITY(1, 1)`、SQLite では `AUTOINCREMENT` になります。ここでは、その既定を変えたい場面と、既定のままでは詰まる場面を扱います。

#### 開始値と増分を変える（SQL Server）

`UseIdentityColumn` に開始値 (seed) と増分 (increment) を渡すと、`IDENTITY` の引数がそのまま変わります。

```csharp
modelBuilder.Entity<Blog>()
    .Property(b => b.Id)
    .UseIdentityColumn(seed: 1000, increment: 10);
```

SQL Server 2022 に対して生成された DDL と、実際に採番された値は次のとおりです。

```sql
CREATE TABLE [Blogs] (
    [Id] int NOT NULL IDENTITY(1000, 10),
    ...
);
```

```text
採番された Id: 1000, 1010
```

#### `IDENTITY` 列に明示的な値を入れる

`IDENTITY` 列を持つエンティティに自分で `Id` を設定して `SaveChanges` すると、次のように失敗します。論理削除した行を元の ID のまま復元したい、といった場面で必ず踏みます。

```text
SqlException: Cannot insert explicit value for identity column in table 'Blogs'
when IDENTITY_INSERT is set to OFF.
```

[公式が案内している回避策](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/value-generation#inserting-explicit-values-into-identity-columns)は、`SET IDENTITY_INSERT` を自分で切り替える方法です。**この設定はトランザクションではなく接続のセッションに対して働く**ため、EF Core の操作と同じ接続で実行する必要があります。次の例では、トランザクションによって接続を開いたままにし、その接続上で SQL の発行と保存を行います。

```csharp
await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

await context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT dbo.Blogs ON", cancellationToken);

try
{
    context.Blogs.Add(new Blog { Id = 999, Name = "復元した行", Url = "https://example.com" });
    await context.SaveChangesAsync(cancellationToken);
}
finally
{
    // 保存の失敗やキャンセル後も OFF を試みる
    await context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT dbo.Blogs OFF", CancellationToken.None);
}

await transaction.CommitAsync(cancellationToken);
```

`ON` が成功した後は、保存が失敗・キャンセルされても `finally` で `OFF` を試みます。後始末には、キャンセル済みの可能性がある要求のトークンではなく `CancellationToken.None` を渡します。

EF Core 10.0.11 / Azure SQL Database の確認例では、成功・保存時の一意制約違反・キャンセルのいずれでも、**同じセッションでの後続の明示的 ID 挿入が拒否され、別テーブルへの `IDENTITY_INSERT ON` は成功する**ことで、元のテーブルが `OFF` に戻ったことを確認できています。`Id = 999` の行は成功時だけ残り、失敗・キャンセル時はロールバックされています。ただし、接続障害などで `OFF` 自体が失敗する可能性はあるため、例外時はこの処理を中断し、同じコンテキストで処理を続行しないでください。

> [!WARNING]
> `SET IDENTITY_INSERT` を `ON` にできるのは、**同一セッション内で同時に 1 つのテーブルだけ**です。同じ接続を使って複数テーブルへ明示的な ID で挿入する場合は、テーブルごとに `ON` と `OFF` を切り替えます。

#### SQL Server の `Guid` 主キーは順序を考慮して生成される

SQL Server プロバイダーで `Guid` 主キーを既定の自動生成にすると、EF Core は値を**クライアント側で**生成します。[`SequentialGuidValueGenerator`](https://learn.microsoft.com/ja-jp/dotnet/api/microsoft.entityframeworkcore.valuegeneration.sequentialguidvaluegenerator?view=efcore-10.0) は、SQL Server のクラスター化キーやインデックス向けに順序を考慮した GUID を作ります。次は、EF Core 10.0.11 で同じコンテキスト・モデル・生成器を使い、`Add` で逐次生成する確認例の先頭 5 件です。

```text
d0ed177a-06dc-467e-0c0b-08df0e98ef11
ccf230bc-240f-4ca3-0c0c-08df0e98ef11
8f2aac5c-4f85-4704-0c0d-08df0e98ef11
0604f1c1-f7b3-4559-0c0e-08df0e98ef11
8d9e01a3-6d16-44fd-0c0f-08df0e98ef11
```

この 5 件では末尾のブロックが共通で、その手前が `0c0b → 0c0f` と増えています。同じ条件の 100,001 個を SQL Server 2022 の `uniqueidentifier` として比較する確認例でも、隣接する 100,000 組の逆転はなく、`ORDER BY Id` と生成順の一致を確認できています。値の順序の確認であり、EF Core による 100,001 行の保存や、インデックスの断片化率の測定ではありません。

> [!WARNING]
> **異なる生成器を混ぜた場合まで、生成順に単調増加するわけではありません。** [公式実装](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/ValueGeneration/SequentialGuidValueGenerator.cs#L28-L43)では、カウンターを生成器のインスタンスごとに保持します。異なるモデルを使い、初期化の間隔を 30 ミリ秒として交互に 2,000 個生成する確認例では、隣接する 1,999 組のうち 999 組で SQL Server の順序の逆転を確認できています。各生成器内と、複数の生成源をまたぐ順序を区別してください。

> [!NOTE]
> 値がクライアントで生成されるため、`SaveChanges` の**前**に `entity.Id` を読めます。データベースへの往復を待たずに、その ID を使って他のエンティティを組み立てられるのが `IDENTITY` との大きな違いです。一方で、`NEWSEQUENTIALID()` のような**データベース側**の既定値は使われません。

#### SQLite の `AUTOINCREMENT` を止める

SQLite では整数の主キーに `AUTOINCREMENT` が付きます。EF Core 10 からは、これを無効化できるようになりました。

```csharp
// 方法 1: SQLite 固有の設定で止める
modelBuilder.Entity<Blog>()
    .Property(b => b.Id)
    .Metadata.SetValueGenerationStrategy(SqliteValueGenerationStrategy.None);

// 方法 2: EF Core で値生成の対象にしない（アプリケーションが値を設定する）
modelBuilder.Entity<Blog>()
    .Property(b => b.Id)
    .ValueGeneratedNever();
```

どちらの構成でも、生成 DDL に `AUTOINCREMENT` が含まれないことを確認できています。

```sql
-- 既定
"Id" INTEGER NOT NULL CONSTRAINT "PK_Blogs" PRIMARY KEY AUTOINCREMENT,

-- 上記いずれかを指定した場合
"Id" INTEGER NOT NULL CONSTRAINT "PK_Blogs" PRIMARY KEY,
```

なお `AUTOINCREMENT` を外しても、SQLite は `INTEGER PRIMARY KEY` を **rowid の別名**として扱うため、値を指定しなければ自動採番されます。完全に止めるには列型を `INTEGER` から `INT` に変える必要がある、と公式は説明しています。

### 監査履歴を自動で残す（テンポラルテーブル）

SQL Server の **テンポラルテーブル (temporal table)** は、テーブルに加えられたすべての変更を自動的に履歴テーブルへ退避する機能です。更新前の行や削除された行が残るため、監査や誤操作からの復元に使えます。EF Core は SQL Server プロバイダーでこれを直接サポートしています。

```csharp
modelBuilder
    .Entity<Employee>()
    .ToTable("Employees", b => b.IsTemporal());
```

[公式のテンポラルテーブルの構成例](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/temporal-tables)は、`IsTemporal` により期間列と履歴テーブルを構成する方法を示しています。次は、上の指定を使う生成 DDL の確認例です。

```sql
DECLARE @historyTableSchema nvarchar(max) = QUOTENAME(SCHEMA_NAME())
EXEC(N'CREATE TABLE [Employees] (
    [Id] uniqueidentifier NOT NULL,
    [Name] nvarchar(max) NOT NULL,
    [Position] nvarchar(max) NOT NULL,
    [Salary] decimal(10,2) NOT NULL,
    [PeriodEnd] datetime2 GENERATED ALWAYS AS ROW END HIDDEN NOT NULL,
    [PeriodStart] datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
    CONSTRAINT [PK_Employees] PRIMARY KEY ([Id]),
    PERIOD FOR SYSTEM_TIME([PeriodStart], [PeriodEnd])
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = ' + @historyTableSchema + N'.[EmployeesHistory]))');
```

`PeriodStart` と `PeriodEnd` という 2 つの期間列と、`EmployeesHistory` という履歴テーブルが自動で作られます。期間列と履歴テーブルの名前は `HasPeriodStart` / `HasPeriodEnd` / `UseHistoryTable` で変更できます。

```csharp
modelBuilder
    .Entity<Employee>()
    .ToTable(
        "Employees",
        b => b.IsTemporal(
            b =>
            {
                b.HasPeriodStart("ValidFrom");
                b.HasPeriodEnd("ValidTo");
                b.UseHistoryTable("EmployeeHistoricalData");
            }));
```

> [!IMPORTANT]
> 期間列に入るのは **SQL Server が生成した UTC 時刻** です。公式ドキュメントも「テンポラルテーブルに関わるすべての操作で UTC を使う」と明記しています。後述するクエリ演算子に渡す時刻も UTC で指定してください。

> [!WARNING]
> **期間列の値は SQL Server に管理させてください。** 公式ドキュメントの「テンポラルテーブルの構成」節は、期間列をシャドウプロパティにマップすることと、格納される UTC 時刻を SQL Server が生成することを説明しています。ここで扱う EF Core 10 の構成も、このシャドウプロパティを使用します。
>
> 次は、EF Core 10.0.11 で新規エンティティの `PeriodStart` に `2000-01-01` を設定する確認例です。保存は成功していますが、格納値は指定値ではなく SQL Server が生成した時刻です。
>
> ```text
> PeriodStart: ValueGenerated=OnAddOrUpdate
> PeriodEnd:   ValueGenerated=OnAddOrUpdate
>
> 設定した値: 2000-01-01 00:00:00
> 実際の値:   2026-09-08 04:13:30
> ```
>
> 通常列と期間値を同時に指定する確認例でも、期間列が INSERT / UPDATE の書込対象に含まれないことを確認できています。**この標準構成では期間値を SQL Server に管理させます。** 過去データの移行全般の可否とは分けて考えてください。

#### 履歴を読む 5 つの演算子

EF Core は履歴を含めて読むための専用の演算子を用意しています。

| 演算子 | 生成される T-SQL | 意味 |
| --- | --- | --- |
| `TemporalAsOf(t)` | `FOR SYSTEM_TIME AS OF 't'` | その時刻に有効だった行 |
| `TemporalAll()` | `FOR SYSTEM_TIME ALL` | 履歴に存在するすべての行 |
| `TemporalFromTo(a, b)` | `FOR SYSTEM_TIME FROM 'a' TO 'b'` | 2 つの時刻の間に有効だった行 |
| `TemporalBetween(a, b)` | `FOR SYSTEM_TIME BETWEEN 'a' AND 'b'` | `FromTo` と同じだが上限で有効になった行も含む |
| `TemporalContainedIn(a, b)` | `FOR SYSTEM_TIME CONTAINED IN ('a', 'b')` | 2 つの時刻の**内側で**有効になり、かつ有効でなくなった行 |

次は、1 件の追加、役職と給与の更新、削除という順で操作する確認例です。結果は **現在のテーブルが 0 件、`TemporalAll` が 2 件**です。

```text
現在のテーブル件数: 0
TemporalAll 件数: 2
    開発/500000.00   2026-09-04 08:50:14.592 〜 2026-09-04 08:50:17.069
    リード/700000.00 2026-09-04 08:50:17.069 〜 2026-09-04 08:50:19.502
```

期間列は既定でシャドウプロパティにマップされるため、値を取り出すには `EF.Property` を使って射影します。

```csharp
var history = await db.Employees
    .TemporalAll()
    .OrderBy(x => EF.Property<DateTime>(x, "PeriodStart"))
    .Select(x => new
    {
        x.Position,
        From = EF.Property<DateTime>(x, "PeriodStart"),
        To = EF.Property<DateTime>(x, "PeriodEnd")
    })
    .ToListAsync();
```

> [!WARNING]
> [公式のテンポラルクエリの説明](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/temporal-tables)では、**テンポラル演算子を使うクエリは既定で追跡なし (no-tracking)** です。また、[シャドウプロパティの値は追跡なしクエリの取得後には参照できません](https://learn.microsoft.com/ja-jp/ef/core/modeling/shadow-properties#accessing-shadow-properties)。この構成の期間列は、上のようにクエリ内で射影してください。今回の `TemporalAsOf` の確認例でも、結果の状態は `Detached` です。通常の追跡クエリで現在の行を読む場合とは区別してください。

次は、同じ境界時刻を指定した `TemporalFromTo`、`TemporalBetween`、`TemporalContainedIn` の確認例です。境界の扱いによる件数の違いを確認できています。

```text
FromTo 件数=2  Between 件数=2  ContainedIn 件数=0
```

#### 削除された行を復元する

[公式の復元例](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/temporal-tables#restoring-historical-data)では、追跡されていない履歴の行を `Add` して現在のテーブルへ再挿入します。このサンプルは GUID 主キーを使用します。`IDENTITY` 主キーでは、[前述の明示値挿入](#identity-列に明示的な値を入れる)への対応が必要です。

```csharp
var employee = await db.Employees
    .TemporalAsOf(timeStamp)
    .SingleAsync(e => e.Name == "佐藤");

db.Add(employee);
await db.SaveChangesAsync();
```

この復元例では、現在のテーブルが 1 件、`TemporalAll` が 3 件となることを確認できています。

> [!WARNING]
> **履歴の復元に GUID 主キーが必須なわけではありません。** [SQL Server の公式の値生成ガイド](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/value-generation#inserting-explicit-values-into-identity-columns)は、`IDENTITY` 列へ明示的な値を挿入する場合、`SaveChangesAsync()` の前に `IDENTITY_INSERT` を有効にする方法を示しています。元の ID で復元する場合も、[前述の明示値挿入の手順](#identity-列に明示的な値を入れる)を考慮してください。今回の `IDENTITY_INSERT` が `OFF` の確認例では、明示値挿入のエラーが確認できています。

#### システムバージョン管理中の履歴は直接更新できない

SQL Server の[公式のテンポラルテーブルの制限](https://learn.microsoft.com/ja-jp/sql/relational-databases/tables/temporal-table-considerations-and-limitations?view=sql-server-ver17)では、履歴テーブルのデータを直接変更できないとされています。`SYSTEM_VERSIONING = ON` の状態で直接 `UPDATE` する対照でも、次の拒否を確認できています。

```text
Cannot update rows in a temporal history table 'V46T3.dbo.EmployeesHistory'.
```

ここで確認したのは、システムバージョン管理が有効な間の直接更新の拒否です。この結果を、管理操作も含めた改ざん防止全般の保証とは扱わないでください。運用設計では、[システムバージョン管理を停止する場合の公式の注意事項](https://learn.microsoft.com/ja-jp/sql/relational-databases/tables/temporal/stop-system-versioning?view=sql-server-ver16)も確認してください。

> [!NOTE]
> 通常のクエリ（テンポラル演算子を使わないクエリ）は `FOR SYSTEM_TIME` を付けないため、履歴は一切見えません。テンポラルテーブルにしても既存のコードの動作は変わりません。

## 3. SQL Server 固有のマッピング

### 空間データ

位置や図形を扱う **空間データ (spatial data)** は、EF Core では **NetTopologySuite (NTS)** ライブラリを介してマップします。プロバイダーごとに対応するパッケージが用意されており、SQL Server では `Microsoft.EntityFrameworkCore.SqlServer.NetTopologySuite` を追加します。

```bash
dotnet add package Microsoft.EntityFrameworkCore.SqlServer.NetTopologySuite
```

```csharp
options.UseSqlServer(connectionString, o => o.UseNetTopologySuite());
```

モデルでは `NetTopologySuite.Geometries` 名前空間の型を使います。

```csharp
public class Shop
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public Point? Location { get; set; }
}
```

> [!WARNING]
> **座標の順番が、地図アプリで見慣れた「緯度, 経度」とは逆です。** NTS の座標は X と Y で表され、公式ドキュメントは **X に経度、Y に緯度**を入れるよう明記しています。取り違えると、緯度に 90 を超える値が入って実行時に失敗します。東京駅（北緯 35.6812 度、東経 139.7671 度）を正しく書くと次のようになります。
>
> ```csharp
> var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);
> var tokyo = factory.CreatePoint(new Coordinate(139.7671, 35.6812));  // X=経度, Y=緯度
> ```
>
> 次は、逆順の `new Coordinate(35.6812, 139.7671)` を SQL Server 2022 に送る確認例のエラーです。
>
> ```text
> Parameter 1 ("@wrong"): The supplied value is not a valid instance of data type geography.
> ```

次は、SQL Server の `geography` 列を含む生成 DDL の確認例です。

```sql
[Location] geography NULL,
```

距離での並べ替えは LINQ で書けます。次の確認例では、`STDistance` への翻訳を確認できています。

```csharp
var origin = new Point(139.7454, 35.6586) { SRID = 4326 };
var q = db.Shops
    .OrderBy(s => s.Location!.Distance(origin))
    .Select(s => new { s.Name, D = s.Location!.Distance(origin) });
```

```sql
SELECT [s].[Name], [s].[Location].STDistance(@origin) AS [D]
FROM [Shops] AS [s]
ORDER BY [s].[Location].STDistance(@origin)
```

東京タワー付近を基準に東京駅と大阪駅を並べる確認例の結果は、それぞれ約 3,186 m と約 401,307 m です。このクエリでは距離をデータベース側で計算するため、全件取得後にアプリケーションで計算する必要はありません。

> [!WARNING]
> **同じ `Distance` でも、データベースで計算させるか .NET 側で計算するかで単位が変わります。** 公式ドキュメントは、**NTS は演算のときに SRID（座標系の識別子）を無視し、平面座標系を仮定する**と明記しています。そのため経度緯度をそのまま渡すと、距離・長さ・面積は**メートルではなく度**で返ります。
>
> 東京駅と大阪駅の 2 点を比較する確認例では、クエリ内の計算は **403,830.7**（メートル）、取得後に .NET 側で呼ぶ `Distance` は **4.381917**（度）です。同じ 2 点でも座標系の扱いが異なります。メートル単位の距離が必要なら、上のように `geography` を使うクエリでデータベースに計算させてください。

#### `geography` と `geometry` を使い分ける

列の型は既定で `geography`（地球を球とみなす座標系）になります。平面座標として扱いたい場合は `HasColumnType` で `geometry` に変更します。次は `INFORMATION_SCHEMA.COLUMNS` による列型の確認例です。

| モデルの記述 | 生成された列型 |
| --- | --- |
| `public Point? Location { get; set; }` | `geography` |
| `.Property(z => z.Shape).HasColumnType("geometry")` | `geometry` |

`geography` を選んだ場合、SQL Server は多角形の頂点の並び順に制約を課します。公式ドキュメントは**外周は反時計回り、内側の穴は時計回り**でなければならず、**NTS がデータベースに送る前に検証する**と説明しています。次は時計回りの多角形を保存する確認例の例外です。同じ多角形を `geometry` 列に保存する対照では、保存成功を確認できています。

```text
System.ArgumentException: When writing a SQL Server geography value,
the shell of a polygon must be oriented counter-clockwise.
```

> [!NOTE]
> **NTS では表現できない図形があります。** 公式ドキュメントは `CircularString`、`CompoundCurve`、`CurvePolygon`（曲線を含む型）が NTS で未対応であることを警告しています。SQL Server 側にこれらのデータがある場合は、`STCurveToLine` で折れ線に変換してから EF Core で扱ってください。また既存データベースからスキャフォールディングする場合は、**先に空間パッケージを追加しておく必要があります。** 後から追加すると、型マッピングが見つからないという警告とともに列がスキップされます。

### hierarchyid で階層構造を扱う

組織図やカテゴリーツリーのような階層構造は、親を指す外部キーで表現するのが一般的ですが、「ある部署の配下すべて」を取るには再帰クエリが要ります。SQL Server の `hierarchyid` 型を使うと、階層内の位置そのものを 1 つの列に格納でき、配下の判定を単純な述語で書けます。

EF Core から使うには専用のパッケージを追加します。

```bash
dotnet add package Microsoft.EntityFrameworkCore.SqlServer.HierarchyId
```

`UseSqlServer` のオプションで `UseHierarchyId` を呼び、プロパティの型に `HierarchyId` を使います。

```csharp
using Microsoft.EntityFrameworkCore;

public class Node
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public HierarchyId Path { get; set; } = null!;
}

// OnConfiguring / DI 登録側
options.UseSqlServer(connectionString, x => x.UseHierarchyId());
```

列は SQL Server の `hierarchyid` 型にマップされます（実測で `sys.types` を確認）。

```sql
CREATE TABLE [Nodes] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NOT NULL,
    [Path] hierarchyid NOT NULL,
    CONSTRAINT [PK_Nodes] PRIMARY KEY ([Id])
);
```

値は `HierarchyId.Parse` で作ります。`/` がルート、`/1/` がその 1 番目の子、`/1/1/` がさらにその子、という表記です。

```csharp
db.AddRange(
    new Node { Name = "全社",   Path = HierarchyId.Parse("/") },
    new Node { Name = "開発部", Path = HierarchyId.Parse("/1/") },
    new Node { Name = "第一課", Path = HierarchyId.Parse("/1/1/") },
    new Node { Name = "営業部", Path = HierarchyId.Parse("/2/") });
```

[公式の hierarchyid ガイド](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/hierarchyid)は、`IsDescendantOf` や `GetLevel` が SQL Server のメソッド呼び出しへ翻訳される例を示しています。次は、本節のモデルでの確認例です。

```csharp
var devPath = HierarchyId.Parse("/1/");
var under = await db.Nodes.Where(n => n.Path.IsDescendantOf(devPath)).ToListAsync();
```

```sql
DECLARE @devPath hierarchyid = hierarchyid::Parse('/1/');

SELECT [n].[Id], [n].[Name], [n].[Path]
FROM [Nodes] AS [n]
WHERE [n].[Path].IsDescendantOf(@devPath) = CAST(1 AS bit)
```

[公式の hierarchyid ガイド](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/hierarchyid)は、**`IsDescendantOf` が自分自身にも `true` を返す**と明記しています。上の確認例の結果も「開発部, 第一課」です。「開発部より下だけ」がほしい場合は、公式のクエリ例と同様に自分自身を除外します。

```csharp
var strictly = await db.Nodes
    .Where(n => n.Path.IsDescendantOf(devPath) && n.Path != devPath)
    .ToListAsync();
```

```sql
-- SQL Server
WHERE [n].[Path].IsDescendantOf(@devPath) = CAST(1 AS bit) AND [n].[Path] <> @devPath
```

この確認例の結果は「第一課」のみです。`GetLevel()` の確認結果は、全社=0 / 開発部=1 / 第一課=2 / 営業部=1 で、階層の深さに対応しています。

> [!NOTE]
> `HierarchyId` 型は `Microsoft.EntityFrameworkCore.SqlServer.Abstractions` パッケージで定義されています。**EF Core 本体には依存しませんが、依存パッケージがないわけではありません。** 10.0.11 の[公式パッケージ定義](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore.SqlServer.Abstractions/EFCore.SqlServer.Abstractions.csproj)と復元結果を照合すると、`Microsoft.SqlServer.Types` などへの依存が確認できます。エンティティ定義用のプロジェクトが `Abstractions` を参照し、クエリ実行側が `HierarchyId` パッケージを参照する、という分離は可能です。

> [!WARNING]
> **`hierarchyid` は、ツリーの整合性まで自動で保証する型ではありません。** 公式の SQL Server 階層データガイドにも、パスの一意性や親の存在を自動で強制しないと明記されています。別の主キーを持つ表の確認例でも、同一パスや親が存在しない子の保存成功を確認できています。パスを主キーにする対照では重複は拒否されています。必要な一意制約と親子の整合性維持は別途設計してください。
>
> また、**部分木の移動は親のパスを書き換えるだけでは完了しません。** 公式の更新例は、対象の子孫も列挙して各行に `GetReparentedValue` を適用しています。この確認例でも、親だけの更新では子孫は元のパスのままで、親子孫の 3 行の更新で部分木全体の移動を確認できています。

### SQL Server 固有の列オプション

#### スパース列

**スパース列 (sparse column)** は、`NULL` の格納を最適化する代わりに、`NULL` でない値の取得コストが上がる列です。TPH 継承（[付録1の「継承のマッピング」](../appendix-efcore-01/index.md#継承のマッピング)）のように「一部の型にしか存在しない列」がテーブルの大半で `NULL` になるケースで効きます。

```csharp
modelBuilder.Entity<SpecialPost>()
    .Property(x => x.Extra)
    .IsSparse();
```

#### UTF-8 の照合順序

SQL Server 2019 以降は `char` / `varchar` 列に UTF-8 の照合順序を指定でき、Unicode を `nvarchar` より小さく格納できる場合があります。EF Core からは、列の型を `varchar` にしたうえで `_UTF8` で終わる照合順序を指定し、あわせて `IsUnicode()` を呼びます。照合順序がクエリの結果そのものを変える点は[付録3の「大文字小文字の区別は照合順序が決める」](../appendix-efcore-03/index.md#大文字小文字の区別は照合順序が決める)で扱います。

この組み合わせは[公式の UTF-8 構成例](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/columns#unicode-and-utf-8)に従っています。`IsUnicode()` は、`HasColumnType` で明示した `varchar(max)` を `nvarchar(max)` に置き換える指定ではありません。

```csharp
modelBuilder.Entity<SpecialPost>()
    .Property(b => b.Name)
    .HasColumnType("varchar(max)")
    .UseCollation("LATIN1_GENERAL_100_CI_AS_SC_UTF8")
    .IsUnicode();
```

EF Core 10.0.11 / Azure SQL Database でこの構成を適用した確認例でも、実際の列型は `varchar` のままで、日本語と絵文字を含む `日本語🌸 café` を保存して再読込した値が一致しています。

次は、この 2 つの構成を組み合わせた生成 DDL の確認例です。

```sql
CREATE TABLE [Posts] (
    [Id] int NOT NULL IDENTITY,
    [Name] varchar(max) COLLATE LATIN1_GENERAL_100_CI_AS_SC_UTF8 NOT NULL,
    [Extra] nvarchar(max) SPARSE NULL,
    CONSTRAINT [PK_Posts] PRIMARY KEY ([Id])
);
```

この例の `sys.columns` の確認結果は、`Extra` が `is_sparse=True`、`Name` が `varchar` 型・照合順序 `Latin1_General_100_CI_AS_SC_UTF8` です。日本語の保存と再読込も、この `varchar` 列で確認できています。

#### メモリ最適化テーブル

テーブル全体をメモリに常駐させる **メモリ最適化テーブル (memory-optimized table)** も、モデル側から指定できます。

```csharp
modelBuilder.Entity<MemItem>().ToTable(t => t.IsMemoryOptimized());
```

[公式のメモリ最適化テーブルの説明](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/memory-optimized-tables)は、`IsMemoryOptimized` を指定したエンティティについて、マイグレーションなどでメモリ最適化テーブルを作成するとしています。本節のモデルの生成 DDL でも、ファイルグループを準備するスクリプトと、次のテーブル定義を確認できています。この定義の主キーは **非クラスター化** です。

```sql
CREATE TABLE [Items] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_Items] PRIMARY KEY NONCLUSTERED ([Id])
) WITH (MEMORY_OPTIMIZED = ON);
```

この生成スクリプトでは、ファイルグループの準備に `SERVERPROPERTY('IsXTPSupported') = 1` などの条件が含まれています。ただし、後続の `CREATE TABLE ... WITH (MEMORY_OPTIMIZED = ON)` はその条件の外側です。**非対応エディションで DDL 全体が何もせず終了する、という意味ではありません。** 生成 DDL の確認であり、非対応エディションへの適用成功の確認ではありません。

### 計算列

データベース側で他の列から値を導出する **計算列 (computed column)** は、`HasComputedColumnSql` で構成します。

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Person>()
        .Property(p => p.DisplayName)
        .HasComputedColumnSql("[FirstName] + ' ' + [LastName]");

    modelBuilder.Entity<Person>()
        .Property(p => p.PersistedName)
        .HasComputedColumnSql("[LastName] + ', ' + [FirstName]", stored: true);
}
```

[公式の計算列の説明](https://learn.microsoft.com/ja-jp/ef/core/modeling/generated-properties#computed-columns)にあるとおり、第 2 引数 `stored` を省略すると **仮想 (virtual) 計算列** になり、値は取得のたびに計算されます。`stored: true` を指定すると **格納 (stored / persisted) 計算列** になり、行の更新のたびに計算されて他の列と同じようにディスクに保存されます。次は SQL Server 2022 向けの生成 DDL の確認例です。

```sql
CREATE TABLE [People] (
    [Id] int NOT NULL IDENTITY,
    [FirstName] nvarchar(max) NOT NULL,
    [LastName] nvarchar(max) NOT NULL,
    [DisplayName] AS [FirstName] + ' ' + [LastName],
    [PersistedName] AS [LastName] + ', ' + [FirstName] PERSISTED,
    CONSTRAINT [PK_People] PRIMARY KEY ([Id])
);
```

`FirstName = "Taro"` / `LastName = "Yamada"` を保存して再読込する確認例では、`DisplayName` は `Taro Yamada`、`PersistedName` は `Yamada, Taro` です。

> [!WARNING]
> 上の構成では、計算列のプロパティに C# 側で値を代入しても、その値でデータベースの計算結果を上書きできません。[公式ドキュメント](https://learn.microsoft.com/ja-jp/ef/core/modeling/generated-properties#overriding-value-generation)も、列の既定値は明示的な値で置き換えられる一方、計算列では同じことはできないと説明しています。**アプリケーションから書き換えたい値には計算列を使わないでください。**
>
> EF Core 10.0.11 / SQL Server 2022 の確認例では、計算列のプロパティだけの変更は保存 SQL を発行せず、`SaveChangesAsync` の戻り値は `0`、DB の値も変更前のままです。`FirstName` を `"Jiro"` に変える対照では、UPDATE の対象は `FirstName` のみで、再読込した計算列は `Jiro Yamada` / `Yamada, Jiro` です。**DB の値と書込 SQL** の確認であり、C# オブジェクトへの代入そのものが取り消されるという意味ではありません。

> [!NOTE]
> 「最終更新日時」を格納計算列で管理したくなりますが、多くのデータベースは計算列に `GETDATE()` のような関数を指定できません。公式ドキュメントはこの用途にはデータベーストリガーを使うよう案内しています。

### Azure SQL の価格レベルをマイグレーションで指定する

Azure SQL Database のサービスレベルや最大サイズは、通常 Azure ポータルや Azure CLI で設定します。ただしスキーマをマイグレーションで管理している場合は、**モデル側で指定してマイグレーションと一緒に適用する**こともできます。

| API | 指定するもの | 生成される T-SQL の句 |
| --- | --- | --- |
| `HasServiceTier` | サービスレベル | `EDITION` |
| `HasDatabaseMaxSize` | データベースの最大サイズ | `MAXSIZE` |
| `HasPerformanceLevel` | パフォーマンスレベル | `SERVICE_OBJECTIVE` |
| `HasPerformanceLevelSql` | エラスティックプールなど、文字列リテラルにならない値 | `SERVICE_OBJECTIVE` |

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.HasServiceTier("GeneralPurpose");
    modelBuilder.HasDatabaseMaxSize("10 GB");
    modelBuilder.HasPerformanceLevel("GP_S_Gen5_1");

    // エラスティックプールに入れる場合は HasPerformanceLevelSql を使う
    // modelBuilder.HasPerformanceLevelSql("ELASTIC_POOL ( name = myelasticpool )");
}
```

このモデルからマイグレーションを生成すると、テーブル作成の前に次の `ALTER DATABASE` が入ります（実測で確認）。

```sql
-- Azure SQL
BEGIN
DECLARE @db_name nvarchar(max) = QUOTENAME(DB_NAME());
EXEC(N'ALTER DATABASE ' + @db_name + ' MODIFY (
MAXSIZE = 10 GB, EDITION = ''GeneralPurpose'', SERVICE_OBJECTIVE = ''GP_S_Gen5_1'' );');
END
```

Azure SQL Database の `GP_S_Gen5_1` / EF Core 10.0.11 の `Initial` マイグレーションの確認例では、適用前の最大サイズは **1 GB**、適用後の [`maxSizeBytes`（最大サイズをバイト単位で表す設定）](https://learn.microsoft.com/ja-jp/rest/api/sql/databases/get?view=rest-sql-2023-08-01#database) は **10,737,418,240 バイト（10 GB）** です。サービス目標名は前後とも `GP_S_Gen5_1` で、容量上限の変更を確認できています。使用済み容量や処理性能の比較ではありません。

> [!WARNING]
> [SQL Server の `ALTER DATABASE` の制約](https://learn.microsoft.com/ja-jp/sql/t-sql/statements/alter-database-transact-sql?view=sql-server-ver17)では、明示的または暗黙的なトランザクション内での実行はできません。次は、このマイグレーションの適用時に確認できている警告です。
>
> ```text
> The migration operation 'BEGIN DECLARE @db_name ...' from migration 'Initial' cannot be
> executed in a transaction. If the app is terminated or an unrecoverable error occurs while
> this operation is being executed then the migration will be left in a partially applied
> state and would need to be reverted manually before it can be applied again.
> Create a separate migration that contains just this operation.
> ```
>
> [EF Core の公式の警告定義](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore.Relational/Properties/RelationalStrings.resx)も、トランザクション外の操作だけを別のマイグレーションに分離するよう案内しています。**この操作だけを含む単独のマイグレーションに分けて**ください。トランザクション外の操作中にアプリケーションが終了した場合などは、部分適用状態から手作業で戻す必要があります。

> [!NOTE]
> 公式ドキュメントは、Azure SQL に接続するときは `UseSqlServer` ではなく `UseAzureSql` を使うよう案内しています。理由は[付録 EF Core 4 の「接続の回復性とトランザクションの併用」](../appendix-efcore-04/index.md#接続の回復性とトランザクションの併用)で説明しているとおり、`UseAzureSql` なら Azure SQL に適した設定で再試行が自動的に構成されるためです。

---

### SQL Server の互換性レベルを明示する

EF Core の SQL Server プロバイダーは、**接続先のバージョンを見に行くのではなく、構成された互換性レベルに基づいて SQL を生成します。** そして `UseSqlServer` の既定値は、接続先が SQL Server 2022 であっても最新にはなりません。

```csharp
options.UseSqlServer(connectionString, o => o.UseCompatibilityLevel(160));
```

公式ドキュメントは「互換性レベルを明示的に構成しなければ、**最新機能を活用しない妥当な既定値**が選ばれる。そのため**明示的に構成することを推奨する**」と述べています。EF Core 10 の既定値は次のとおりです。

| メソッド | 既定の互換性レベル | 相当する製品 |
| --- | --- | --- |
| `UseSqlServer` | 150 | SQL Server 2019 |
| `UseAzureSql` | 170 | Azure SQL Database |

この差は生成 SQL に影響します。`Math.Max` / `Math.Min` は互換性レベル 160 以上で `GREATEST` / `LEAST` に翻訳されます。次の `Where` 内の `Math.Max` で、EF Core 側の互換性レベルだけを変える確認例では、**160 未満の `ToQueryString()` は SQL 翻訳時の例外**です。SQL 翻訳の確認であり、データベースへのクエリ実行ではありません。最終 `Select` で許されるクライアント評価とは区別してください。

```csharp
// Rating と Rating2 の大きいほうが 4 を超えるブログ
var sql = db.Blogs.Where(b => Math.Max(b.Rating, b.Rating2) > 4).ToQueryString();
```

| EF Core 側の構成 | 結果 |
| --- | --- |
| 未指定（既定の 150） | `InvalidOperationException`（翻訳できない） |
| `UseCompatibilityLevel(120)` 〜 `(150)` | 同上 |
| `UseCompatibilityLevel(160)` | `GREATEST` に翻訳される |

```sql
-- SQL Server（互換性レベル 160 以上）
SELECT [b].[Id], [b].[Name], [b].[Rating], [b].[Rating2]
FROM [Blogs] AS [b]
WHERE GREATEST([b].[Rating], [b].[Rating2]) > 4
```

> [!WARNING]
> **SQL Server 2022 を接続先に指定しても、EF Core の互換性レベルは自動では変わりません。** 上の翻訳結果は EF Core 側の設定に基づくもので、接続先のバージョンやデータベース側の設定を検査した結果ではありません。逆に、データベースが対応しない機能を EF Core 側だけで有効にすると、生成された SQL を実行できない場合があります。公式も「これは EF 自身の構成であり、実際のデータベースの互換性レベルには影響しない」と明記しています。接続先が対応するレベルを構成してください。データベースの現在の値は次の SQL で確認できます。
>
> ```sql
> -- SQL Server
> SELECT name, compatibility_level FROM sys.databases WHERE name = DB_NAME();
> ```

> [!NOTE]
> `GREATEST` と `LEAST` は SQL Server 2022 で追加された関数です。EF Core 11 では `UseSqlServer` の既定値が 160 に変わることが破壊的変更として予告されています。古い SQL Server に接続しているアプリケーションでは、そのときに `UseCompatibilityLevel(150)` の明示が必要になります。**いま明示しておけば、この破壊的変更の影響を受けません。**

---

## 4. モデル全体にかかる構成

### コマンドのタイムアウト

EF Core の `CommandTimeout` は **既定では未設定 (`null`)** で、この場合は ADO.NET プロバイダーの既定値が使われます。`Microsoft.Data.SqlClient` の `SqlCommand.CommandTimeout` は、公式ドキュメントに「既定は 30 秒」と記載されています。既定構成の確認例でも、`db.Database.GetCommandTimeout()` は `null`、ログは `CommandTimeout='30'` です。

設定方法は 2 つあります。

```csharp
// 1. DbContext 全体の既定として構成する
builder.Services.AddDbContext<BloggingContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions => sqlOptions.CommandTimeout(120)));

// 2. 特定の処理だけ実行時に変更する
context.Database.SetCommandTimeout(180);
```

次は、`CommandTimeout` を 3 秒に設定し、10 秒待機するコマンドを実行する確認例です。この条件での経過時間は約 3.2 秒で、`SqlException` のエラー番号 `-2`（タイムアウト）を確認できています。

```text
SqlException Number=-2: 実行タイムアウトの期限が切れました。
操作完了前にタイムアウト期間が過ぎたか、サーバーが応答していません。
```

> [!TIP]
> `CommandTimeout` は 1 つのコマンドの実行時間の上限で、接続の確立を待つ時間の上限である接続文字列の `Connect Timeout` とは別物です。レポート生成や一括更新のような長時間かかる処理だけを対象に `SetCommandTimeout` で個別に延ばすほうが、全体の既定値を大きくするより安全です。

### 一括構成（規約前の構成）

`HasMaxLength` や `HasPrecision` を全エンティティのプロパティに個別に書いていくのは現実的ではありません。EF Core には、CLR の型ごとにマッピング設定を一度だけ指定し、モデルの構築時にその型のすべてのプロパティへ自動適用する仕組みがあります。公式ドキュメントではこれを **規約前のモデル構成 (pre-convention model configuration)** と呼び、`DbContext` の `ConfigureConventions` をオーバーライドして記述します。

```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
{
    // すべての string プロパティを nvarchar(200) にする
    configurationBuilder.Properties<string>()
        .HaveMaxLength(200);

    // すべての decimal プロパティの精度を固定する
    configurationBuilder.Properties<decimal>()
        .HavePrecision(18, 2);
}
```

`OnModelCreating` の `Property(...)` 系メソッドが `Has` で始まるのに対し、`ConfigureConventions` 側は **`Have` で始まる** 点に注意してください（`HasMaxLength` ではなく `HaveMaxLength`）。

実際に SQL Server 向けの DDL を生成して比較すると、文字列列の型が変わることを確認できます。

```sql
-- ConfigureConventions なし
[Url] nvarchar(max) NOT NULL,
[Title] nvarchar(max) NOT NULL,

-- ConfigureConventions で HaveMaxLength(200) を指定
[Url] nvarchar(200) NOT NULL,
[Title] nvarchar(200) NOT NULL,
```

> [!NOTE]
> 指定する型には、具体的な型だけでなく基底型・インターフェイス・ジェネリック型定義も使えます。複数の構成が一致する場合は、公式ドキュメントによると「インターフェイス → 基底型 → ジェネリック型定義 → 非 NULL 許容の値型 → 完全一致の型」の順に、**具体性の低いものから順に適用**されます。したがって、より具体的な指定が後勝ちになります。

> [!TIP]
> **Django** の `models.CharField(max_length=200)` は、モデル側に最大長を指定する書き方です（[Microsoft の移行例](https://learn.microsoft.com/ja-jp/sql/connect/python/mssql-django/migrate-from-postgresql?view=sql-server-ver17)）。EF Core では `ConfigureConventions` により、複数のプロパティに適用する既定値をコード 1 か所にまとめられます。

### グローバルクエリフィルターと名前付きクエリフィルター

論理削除（ソフトデリート）やマルチテナントのように、「すべてのクエリに自動的に適用したい条件」はグローバルクエリフィルターで表現します。

```csharp
public class Blog
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public bool IsDeleted { get; set; }
    public int TenantId { get; set; }
}
```

このエンティティに対して、論理削除された行を除外するフィルターを設定します。

```csharp
modelBuilder.Entity<Blog>().HasQueryFilter(b => !b.IsDeleted);
```

これ以降、`context.Blogs` に対するクエリはすべて `WHERE [b].[IsDeleted] = CAST(0 AS bit)` が自動的に付与されます。フィルターを無効にしたい特定のクエリでは `IgnoreQueryFilters()` を呼びます。

EF Core 10 では **名前付きクエリフィルター (Named Query Filters)** が導入され、1 つのエンティティ型に複数のフィルターを設定して個別に無効化できるようになりました。

テナント ID は、[公式のマルチテナントの例](https://learn.microsoft.com/ja-jp/ef/core/querying/filters#using-context-data---multi-tenancy)と同じく、コンテキストのインスタンスに保持させます。次の `tenantId` には、認証済みユーザーなどの信頼できる情報から決めた現在のテナント ID を、コンテキストの作成時に渡します。

```csharp
public class TenantBlogContext(
    DbContextOptions<TenantBlogContext> options,
    int tenantId) : DbContext(options)
{
    public DbSet<Blog> Blogs => Set<Blog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Blog>()
            .HasQueryFilter("SoftDeletionFilter", b => !b.IsDeleted)
            .HasQueryFilter("TenantFilter", b => b.TenantId == tenantId);
    }
}
```

この `TenantBlogContext` は、本編の通常の `AddDbContext<TenantBlogContext>` だけでは DI から解決できません。DI は未登録の `int tenantId` を用意できないためです。この例では、[オプションを渡して `new` で生成する公式の方法](https://learn.microsoft.com/ja-jp/ef/core/dbcontext-configuration/#basic-dbcontext-initialization-with-new)を使い、テナント ID を引数に取るファクトリを用意します。

```csharp
public static class TenantBlogContextFactory
{
    public static TenantBlogContext Create(
        string connectionString, int authenticatedTenantId)
    {
        var options = new DbContextOptionsBuilder<TenantBlogContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new TenantBlogContext(options, authenticatedTenantId);
    }
}
```

呼び出し側では、アプリケーションの構成から取得した `connectionString` と、信頼できる認証情報から決めた `authenticatedTenantId` を渡します。リクエストに含まれるテナント ID を、そのユーザーが利用できるか確認せずに渡してはいけません。ここでは `TenantBlogContext` を DI から直接受け取るのではなく、次のように生成し、呼び出し側で `await using` により破棄します。

```csharp
await using var context = TenantBlogContextFactory.Create(
    connectionString, authenticatedTenantId);
```

この `context` に対して、名前を付けたフィルターのうち無効化したいものだけを個別に選べます。

```csharp
// 論理削除のフィルターだけを無効化し、テナントのフィルターは維持する
var allBlogs = await context.Blogs
    .IgnoreQueryFilters(["SoftDeletionFilter"])
    .ToListAsync(cancellationToken);
```

`DbSet.Remove` を呼んだときに実際の削除ではなく `IsDeleted` を立てたい場合は、[公式の論理削除の例](https://learn.microsoft.com/ja-jp/ef/core/querying/filters#basic-example---soft-deletion)と同様に、保存前に `Deleted` を `Modified` へ変換します。次のメソッドを `TenantBlogContext` に追加すると、同期・非同期のどちらの保存経路でも同じ変換を適用できます。

オーバーライドするのは、`acceptAllChangesOnSuccess` を受け取る `SaveChanges(bool)` と `SaveChangesAsync(bool, CancellationToken)` です。[`DbContext` の公式実装](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore/DbContext.cs)では、`SaveChanges()` と `SaveChangesAsync(CancellationToken)` もそれぞれこれらのオーバーロードへ処理を委譲します。

```csharp
public override int SaveChanges(bool acceptAllChangesOnSuccess)
{
    ApplySoftDelete();
    return base.SaveChanges(acceptAllChangesOnSuccess);
}

public override Task<int> SaveChangesAsync(
    bool acceptAllChangesOnSuccess,
    CancellationToken cancellationToken = default)
{
    ApplySoftDelete();
    return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
}

private void ApplySoftDelete()
{
    ChangeTracker.DetectChanges();

    foreach (var entry in ChangeTracker.Entries<Blog>().Where(e => e.State == EntityState.Deleted))
    {
        entry.State = EntityState.Modified;
        entry.CurrentValues[nameof(Blog.IsDeleted)] = true;
    }
}
```

> [!NOTE]
> 対象は、変更追跡を経由して `SaveChanges` / `SaveChangesAsync` で保存する操作です。[`ExecuteDeleteAsync` は変更トラッカーを使わずに直接削除する](https://learn.microsoft.com/ja-jp/ef/core/saving/execute-insert-update-delete#executedelete)ため、このオーバーライドでは論理削除に変換されません。

> [!WARNING]
> クエリフィルターを設定したエンティティが、必須のリレーションシップ（外部キーが NULL を許さない関係）の「1」側になっている場合、EF Core はモデル検証時に次の警告を出します。
>
> ```text
> Entity 'Blog' has a global query filter defined and is the required end of a relationship
> with the entity 'Post'. This may lead to unexpected results when the required entity is
> filtered out.
> ```
>
> 親 (`Blog`) がフィルターで除外されているのに子 (`Post`) が除外されないと、`Post` を起点にしたクエリで親が読み込めず、予期しない結果になります。ナビゲーションを省略可能にするか、関係する両方のエンティティに対応するフィルターを定義してください。

次は、`fish` を含む `Blog` と含まない `Blog` に投稿を 3 件ずつ持たせた SQL Server 2022 での確認例です。この必須関係とフィルターの構成では、**`Include` の追加により取得件数が減る**ことを確認できています。

```csharp
var withoutInclude = await context.Posts.ToListAsync();                       // 6 件
var withInclude    = await context.Posts.Include(p => p.Blog).ToListAsync();  // 3 件
```

必須ナビゲーションは「関連エンティティが必ず存在する」ことを意味するため、EF Core は `INNER JOIN` を組み立てます。フィルターで除外された `Blog` に紐づく `Post` は、この結合で一緒に消えます。

```sql
-- SQL Server
SELECT [p].[PostId], [p].[BlogId], [p].[Title], [b0].[BlogId], [b0].[Url]
FROM [Posts] AS [p]
INNER JOIN (
    SELECT [b].[BlogId], [b].[Url]
    FROM [Blogs] AS [b]
    WHERE [b].[Url] LIKE N'%fish%'
) AS [b0] ON [p].[BlogId] = [b0].[BlogId]
```

公式ドキュメントが挙げている 2 つの対処を、それぞれ実測した結果です。

| 構成 | `Include` なし | `Include` あり |
| --- | --- | --- |
| 必須ナビゲーションのまま（既定） | 6 件 | **3 件** |
| `IsRequired(false)` で省略可能にする | 6 件 | 6 件 |
| `Post` 側にも同じフィルターを定義する | 3 件 | 3 件 |

省略可能にすると `LEFT JOIN` になるため件数は減りませんが、`Post.Blog` が `null` になり得ます。**件数を揃えたいのか、親を必ず取りたいのかで選び分けてください。**

> [!WARNING]
> **論理削除を採用するなら、データベース側のカスケード削除を構成してはいけません。** 公式ドキュメントは「エンティティを論理削除する場合はデータベースでカスケード削除を構成しないこと。誤って論理削除ではなく実際に削除されてしまう可能性がある」と明記しています。
>
> 次は、`Blog` と `Post` の両方に `IsDeleted` のフィルターを設定し、外部キーを既定の `ON DELETE CASCADE` のまま親を `Remove` する SQL Server 2022 での確認例です。
>
> | 操作 | `Blogs` の実行数 | `Posts` の実行数 |
> | --- | --- | --- |
> | `IsDeleted = true` にして保存（論理削除） | 1 | 2 |
> | 親を `Remove` して保存（物理削除） | **0** | **0** |
>
> 論理削除の運用に 1 か所でも物理削除の経路が混ざると、データベース側のカスケードによって子まで消えます。論理削除を使う場合は `OnDelete(DeleteBehavior.Restrict)` などでデータベースのカスケードを外し、削除は必ずフラグの更新として行ってください。

> [!NOTE]
> EF Core 10 の名前付きクエリフィルターでは、複数の条件に名前を付け、必要なフィルターだけを無効にできます。論理削除とテナントの条件を分けて管理したい場合に使います。

---

### コード分析ルールがエンティティ定義と衝突する

プロジェクトで `<AnalysisLevel>latest-all</AnalysisLevel>` のように厳しいコード分析を有効にすると、上のエンティティ定義に対して次のような警告が出ます（実測で確認）。

| ルール | 内容 |
| --- | --- |
| `CA1002` | `List<Post>` ではなく `Collection<T>` を公開すべき |
| `CA2227` | コレクションプロパティのセッターを削除して読み取り専用にすべき |
| `CA1056` | `Url` プロパティは `string` ではなく `Uri` にすべき |

**EF Core のエンティティだから、これらのルールを一律に無効化する必要があるわけではありません。** [公式のナビゲーションの説明](https://learn.microsoft.com/ja-jp/ef/core/modeling/relationships/navigations#collection-navigations)は、コレクションナビゲーションにセッターは不要としています。また `Uri` には[組み込みの値コンバーター](https://learn.microsoft.com/ja-jp/ef/core/modeling/value-conversions#built-in-converters)があり、標準でマッピングできない型ではありません。

セッターのない `List<Post>` と明示的なコンバーター構成のない `Uri` を持つモデルの確認例（EF Core 10.0.11 / SQLite）でも、別の `DbContext` での再読込時に、子のコレクション・親子の参照・URI の値を確認できています。警告への対応は公開設計に合わせ、意図して現在の形を維持する場合だけ、次のように抑制を対象フォルダーへ限定します。

```ini
# プロジェクト直下の Models フォルダーを対象にする場合
[Models/*.cs]
dotnet_diagnostic.CA1002.severity = none
dotnet_diagnostic.CA2227.severity = none
dotnet_diagnostic.CA1056.severity = none
```

[公式のアナライザー構成の説明](https://learn.microsoft.com/ja-jp/dotnet/fundamentals/code-analysis/configuration-files)では、`.editorconfig` の配置とセクションヘッダーで適用対象を選びます。上の例は、プロジェクト直下に `.editorconfig` と `Models` フォルダーを置く構成です。実際の配置に合わせ、意図したファイルに抑制が適用されているかをビルドで確認してください。

## 5. 参考ドキュメント

- [テンポラルテーブルの考慮事項と制限事項 | Microsoft Learn](https://learn.microsoft.com/ja-jp/sql/relational-databases/tables/temporal-table-considerations-and-limitations?view=sql-server-ver17)
- [エンティティのプロパティ：有効桁数と小数点以下桁数 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/modeling/entity-properties#precision-and-scale)
- [decimal と numeric | Microsoft Learn](https://learn.microsoft.com/ja-jp/sql/t-sql/data-types/decimal-and-numeric-transact-sql?view=sql-server-ver17)
- [SqlClient 6.1.6 の decimal パラメーター処理 | GitHub](https://github.com/dotnet/SqlClient/blob/v6.1.6/src/Microsoft.Data.SqlClient/netcore/src/Microsoft/Data/SqlClient/TdsParser.cs#L9737-L9777)
- [外部キーと主キー | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/modeling/relationships/foreign-and-principal-keys)
- [キー | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/modeling/keys)
- [キーなしエンティティ型 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/modeling/keyless-entity-types)
- [シャドウプロパティとインデクサープロパティ | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/modeling/shadow-properties)
- [バッキングフィールド | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/modeling/backing-field)
- [シーケンス | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/modeling/sequences)
- [Add と AddAsync の違い | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/change-tracking/miscellaneous#add-versus-addasync)
- [HiLoValueGenerator クラス | Microsoft Learn](https://learn.microsoft.com/ja-jp/dotnet/api/microsoft.entityframeworkcore.valuegeneration.hilovaluegenerator-1?view=efcore-10.0)
- [生成されるプロパティ値 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/modeling/generated-properties)
- [SQL Server / Azure SQL のテンポラルテーブル | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/temporal-tables)
- [SQL Server プロバイダー固有の列機能 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/columns)
- [SQL Server の HierarchyId | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/hierarchyid)
- [階層データ | Microsoft Learn](https://learn.microsoft.com/ja-jp/sql/relational-databases/hierarchical-data-sql-server?view=sql-server-ver16)
- [SQL Server のメモリ最適化テーブルのサポート | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/memory-optimized-tables)
- [SQL Server プロバイダーの値生成 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/value-generation)
- [空間データ | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/modeling/spatial)
- [SQL Server プロバイダーの空間データ | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/spatial)
- [一括構成 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/modeling/bulk-configuration)
- [グローバルクエリフィルター | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/querying/filters)
- [SQL Server プロバイダーのインデックス | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/indexes)
- [ALTER DATABASE の制約 | Microsoft Learn](https://learn.microsoft.com/ja-jp/sql/t-sql/statements/alter-database-transact-sql?view=sql-server-ver17)
- [非トランザクション移行の警告定義 | GitHub](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore.Relational/Properties/RelationalStrings.resx)
- [コード分析の構成ファイル | Microsoft Learn](https://learn.microsoft.com/ja-jp/dotnet/fundamentals/code-analysis/configuration-files)
