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

SQL Server で実測した DDL では、`AK_` で始まる名前の一意制約が作られました。

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

存在しないメールアドレスで `Order` を保存しようとすると、データベース側で拒否されました。

```text
The INSERT statement conflicted with the FOREIGN KEY constraint "FK_Orders_Users_UserEmail".
```

> [!TIP]
> **単に一意性を強制したいだけなら、代替キーではなく一意インデックス (`HasIndex(...).IsUnique()`) を使ってください。** 公式ドキュメントも同じ指針を示しています。代替キーが一意インデックスと違うのは、外部キーの参照先にできる点です。
>
> なお、代替キーは明示的に構成しなくても導入されることがあります。一意インデックスだけを定義したプロパティを `HasPrincipalKey` の対象に指定したところ、EF Core が代替キーを自動的に追加し、`AK_Users_Email` 制約と `IX_Users_Email` 一意インデックスの両方が生成されました。

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

SQL Server 2022 に対して生成された DDL は次のとおりです（実測）。`HasAlternateKey` を書いていないのに `AK_Posts_AlternateKey` が作られている点に注目してください。前述のとおり `HasPrincipalKey` が代替キーを自動で導入します。

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

> [!WARNING]
> `UsingEntity` には引数を 1 つだけ取るオーバーロードもありますが、そちらで `HasForeignKey` と `HasPrincipalKey` を同時に指定すると、**規約による主キー参照の外部キーが残ったまま、代替キー参照の列が追加で作られます**。実測では `PostTag` に `PostsId` と `PostsAlternateKey` の両方が生成され、後者は常に `NULL` のままでした。代替キーを使うときは公式サンプルどおり 2 引数のオーバーロードを使い、`HasPrincipalKey` だけを指定してください。

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
> 依存側のクエリに `IS NOT NULL` の条件が自動的に付いている点に注目してください。公式ドキュメントによると、**依存エンティティが使う列がすべて `NULL` の場合、EF Core はそのインスタンスを作りません。** 実測でも `Order` だけを保存した後、`Orders` は 2 行なのに `Detailed` は 1 件しか返りませんでした。
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

SQL Server での実測では 2 つのテーブルが作られ、クエリは `INNER JOIN` になり、1 件の保存で 2 行が書き込まれました（影響行数 2）。

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

このとき **クエリ後の追跡エントリー数は 0 でした。** 公式ドキュメントの「`DbContext` で変更が追跡されることはなく、したがってデータベースに挿入・更新・削除されることもない」という記述どおりの動作です。試しに `Add` して保存しようとすると、次の例外になりました。

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
> - **通常のエンティティ型からキーなしエンティティ型へのナビゲーションプロパティを持てない。** 実際に書いてみると、モデル構築の時点で `Unable to determine the relationship represented by navigation 'NavBlog.Counts' of type 'BlogPostCount'.` という例外になりました
> - リレーションシップの主体側になれない
> - 継承階層は作れるが、TPH としてしかマップできない（継承の 3 つの方式は[付録1の「継承のマッピング」](/appendix-efcore-01/#継承のマッピング)を参照）
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
> **EF Core はチェック制約を保存前に検証しません。** 制約に違反する値で `SaveChangesAsync` を呼ぶと、データベースがエラーを返し、EF Core はそれを `DbUpdateException` に包んで投げます。実際に `Price = -1` で保存すると、内部例外は次のようになりました（実測）。
>
> ```text
> SqlException: The INSERT statement conflicted with the CHECK constraint "CK_Product_Price".
> The conflict occurred in database "CcTest", table "dbo.Products", column 'Price'.
> ```
>
> チェック制約は「アプリケーションのバグや別経路からの書き込みでも不正なデータが入らない」という最後の砦です。利用者に見せるバリデーションは、アプリケーション側でも別途実装してください。

> [!TIP]
> 「主キーは正の数」「終了日は開始日以降」のようによくあるチェック制約は、コミュニティパッケージの [EFCore.CheckConstraints](https://github.com/efcore/EFCore.CheckConstraints) を使うと規約として自動生成できます。公式ドキュメントもこのパッケージを紹介しています。

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
> 実は、**外部キーを明示的に書かなかった場合の外部キーもシャドウプロパティ**です。公式ドキュメントは「シャドウプロパティは外部キーに最もよく使われる」と述べています。[エンティティクラスの定義](../08-entity-framework-core/index.md#エンティティクラスの定義)では `Post` に `BlogId` を明示していますが、あえてこれを省略して `Blog` 側の `List<Post> Posts` だけでモデルを作ったところ、実測では `Posts` テーブルに `BlogId` 列と外部キー制約、インデックスが生成され、そのプロパティは `IsShadowProperty() == true` でした。
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

    public void SetPrice(decimal value)      // 変更は必ずこのメソッド経由
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

これにより、データベースから読み込むときは `SetPrice` を経由せずに `_price` へ直接書き込まれます。実測でも、データベースから 1 件読み出したあとの `SetPrice` の呼び出し回数は、保存時の 1 回のままで増えませんでした。ドメインの不変条件を守るための検証は「アプリケーションからの変更」にだけ適用され、「データベースからの復元」では走らない、ということです。

> [!WARNING]
> **`HasField` を省略すると、この例では列そのものが作られません。** 公式ドキュメントは規約で `_price` のようなフィールドが発見されると説明していますが、その前提として「**getter と setter を持つ public プロパティ**が規約でモデルに含まれる」という規約があります。上の `Price` は getter しか持たないため、そもそもモデルに含まれず、実測でも `Products` テーブルには `Id` 列しか生成されませんでした。読み取り専用プロパティを永続化したい場合は、`HasField` で明示的に構成してください。

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

SQL Server 2022 に対して実行したところ、次の DDL が生成されました（実測）。

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

公式が案内している回避策は、`SET IDENTITY_INSERT` を自分で切り替える方法です。**この設定はトランザクションではなく接続に対して働く**ため、EF Core の操作と同じ接続で実行する必要があります。`DbContext` 経由で SQL を発行すれば同じ接続が使われます。

```csharp
using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

await context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT dbo.Blogs ON", cancellationToken);

context.Blogs.Add(new Blog { Id = 999, Name = "復元した行" });
await context.SaveChangesAsync(cancellationToken);

await context.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT dbo.Blogs OFF", cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

この手順で `Id = 999` の行が挿入できることを SQL Server 2022 で確認しました。

> [!WARNING]
> `SET IDENTITY_INSERT` は**同時に 1 つのテーブルにしか設定できません。** 複数テーブルへ明示的な ID で挿入する場合は、テーブルごとに `ON` と `OFF` を往復させる必要があります。

#### `Guid` の主キーは連番になる

主キーを `Guid` にすると、EF Core は値を**クライアント側で**生成します。このとき使われる `SequentialGuidValueGenerator` は、完全なランダム値ではなく **SQL Server の `uniqueidentifier` の並び順で単調増加する値**を作ります。実際に 5 件を連続して挿入したときの値は次のようになりました。

```text
438d4606-b71f-460f-9fbd-08df0a6347c0
5461ab70-518b-4de5-9fbe-08df0a6347c0
f558d325-061a-4363-9fbf-08df0a6347c0
a1dcee1b-8378-46a7-9fc0-08df0a6347c0
6cc905b7-80ec-403e-9fc1-08df0a6347c0
```

先頭は毎回変わりますが、末尾のブロックが共通で、その手前が `9fbd → 9fc1` と 1 ずつ増えています。この状態でデータベース側に `ORDER BY Id` で並べ替えさせると、挿入した順序どおりに返ってきました。クラスター化インデックスの断片化を避けるための設計です。

> [!NOTE]
> 値がクライアントで生成されるため、`SaveChanges` の**前**に `entity.Id` を読めます。データベースへの往復を待たずに、その ID を使って他のエンティティを組み立てられるのが `IDENTITY` との大きな違いです。一方で、`NEWSEQUENTIALID()` のような**データベース側**の既定値は使われません。

#### SQLite の `AUTOINCREMENT` を止める

SQLite では整数の主キーに `AUTOINCREMENT` が付きます。EF Core 10 からは、これを無効化できるようになりました。

```csharp
// 方法 1: SQLite 固有の設定で止める
modelBuilder.Entity<Blog>()
    .Property(b => b.Id)
    .Metadata.SetValueGenerationStrategy(SqliteValueGenerationStrategy.None);

// 方法 2: 値生成そのものを行わない（アプリケーションが値を設定する）
modelBuilder.Entity<Blog>()
    .Property(b => b.Id)
    .ValueGeneratedNever();
```

どちらでも `AUTOINCREMENT` が消えることを確認しました。

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

これだけで、生成される DDL は通常のテーブルとはまったく別物になります（実測）。

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
> **期間プロパティに自分で値を設定してはいけません。** 公式ドキュメントは「期間プロパティは自動的に `ValueGenerated.OnAddOrUpdate` で構成されるため、値は常に SQL Server が生成する。エンティティの挿入や更新のときに値を設定する必要はなく、**設定すべきでもない**」と述べています。
>
> 問題は、設定しても**例外にならず黙って無視される**ことです。シャドウプロパティ経由で `PeriodStart` に `2000-01-01` を設定して保存したところ、保存自体は成功し、実際に格納された値は SQL Server が生成した現在時刻でした（実測）。
>
> ```text
> PeriodStart: ValueGenerated=OnAddOrUpdate
> PeriodEnd:   ValueGenerated=OnAddOrUpdate
>
> 設定した値: 2000-01-01 00:00:00
> 実際の値:   2026-09-08 04:13:30
> ```
>
> 「過去のデータを履歴として流し込む」といった用途に、EF Core 経由のテンポラルテーブルは使えません。

#### 履歴を読む 5 つの演算子

EF Core は履歴を含めて読むための専用の演算子を用意しています。

| 演算子 | 生成される T-SQL | 意味 |
| --- | --- | --- |
| `TemporalAsOf(t)` | `FOR SYSTEM_TIME AS OF 't'` | その時刻に有効だった行 |
| `TemporalAll()` | `FOR SYSTEM_TIME ALL` | 履歴に存在するすべての行 |
| `TemporalFromTo(a, b)` | `FOR SYSTEM_TIME FROM 'a' TO 'b'` | 2 つの時刻の間に有効だった行 |
| `TemporalBetween(a, b)` | `FOR SYSTEM_TIME BETWEEN 'a' AND 'b'` | `FromTo` と同じだが上限で有効になった行も含む |
| `TemporalContainedIn(a, b)` | `FOR SYSTEM_TIME CONTAINED IN ('a', 'b')` | 2 つの時刻の**内側で**有効になり、かつ有効でなくなった行 |

行を 1 件追加し、役職と給与を更新し、最後に削除する、という操作を行ったあとで確認すると次のようになりました（実測）。**現在のテーブルは 0 件なのに、`TemporalAll` は 2 件返します。**

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
> **テンポラル演算子を使ったクエリは既定で追跡なし (no-tracking) です。** 実測でも `TemporalAsOf` の結果は `Detached` でした。そのため `db.Entry(entity).Property<DateTime>("PeriodStart")` のようにエンティティのエントリー経由で期間列を読もうとしても値は取れず、`0001-01-01` が返ります。期間列は必ず上のように射影で取り出してください。

同じ時点を指定する `TemporalFromTo` と `TemporalBetween`、`TemporalContainedIn` は境界の扱いが違うため、実測でも結果件数が分かれました。

```text
FromTo 件数=2  Between 件数=2  ContainedIn 件数=0
```

#### 削除された行を復元する

テンポラル演算子の結果は追跡されていないので、そのまま `Add` すれば現在のテーブルへ入れ直せます。

```csharp
var employee = await db.Employees
    .TemporalAsOf(timeStamp)
    .SingleAsync(e => e.Name == "佐藤");

db.Add(employee);
await db.SaveChangesAsync();
```

実測では削除済みだった行が復元され、現在のテーブルが 1 件に戻り、`TemporalAll` は 3 件になりました。

> [!WARNING]
> 主キーが `IDENTITY` 列の場合、この復元は `Cannot insert explicit value for identity column in table 'Employees' when IDENTITY_INSERT is set to OFF.` で失敗します。復元を運用として想定するなら、公式サンプルと同じく **主キーをアプリケーション側で生成する型（`Guid` など）にしておく**必要があります。

#### 履歴は書き換えられない

履歴テーブルを直接 `UPDATE` しようとすると、SQL Server 自身が拒否します（実測）。

```text
Cannot update rows in a temporal history table 'V46T3.dbo.EmployeesHistory'.
```

監査ログをアプリケーション側のテーブルで自前実装すると、そのテーブルも普通のテーブルなので書き換えられてしまいます。テンポラルテーブルはこの点がデータベースエンジンによって保証されます。

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
> 逆に `new Coordinate(35.6812, 139.7671)` と書いて SQL Server 2022 に送ると、次の例外になりました（実測で確認）。
>
> ```text
> Parameter 1 ("@wrong"): The supplied value is not a valid instance of data type geography.
> ```

実測した DDL では、SQL Server の `geography` 型の列が作られました。

```sql
[Location] geography NULL,
```

距離での並べ替えは、そのまま LINQ で書けます。実測したクエリは `STDistance` に変換されました。

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

東京タワー付近を基準に東京駅と大阪駅を並べたところ、それぞれ約 3,186 m と約 401,307 m という結果になりました。距離の計算がデータベース側で行われるため、全件を取得してからアプリケーションで計算する必要がありません。

> [!WARNING]
> **同じ `Distance` でも、データベースで計算させるか .NET 側で計算するかで単位が変わります。** 公式ドキュメントは、**NTS は演算のときに SRID（座標系の識別子）を無視し、平面座標系を仮定する**と明記しています。そのため経度緯度をそのまま渡すと、距離・長さ・面積は**メートルではなく度**で返ります。
>
> 東京駅と大阪駅の 2 点で実測したところ、上のようにクエリの中で計算させると **403,830.7**（メートル）、いったんエンティティを読み込んでから .NET 側で `Distance` を呼ぶと **4.381917**（度）になりました。同じ 2 点なのに値がまったく違います。**度をメートルに換算する定数はありません**（緯度によって 1 度の長さが変わるため）。距離が必要なら、クエリの中で計算してデータベースに評価させてください。

#### `geography` と `geometry` を使い分ける

列の型は既定で `geography`（地球を球とみなす座標系）になります。平面座標として扱いたい場合は `HasColumnType` で `geometry` に変更します。どちらになるかを `INFORMATION_SCHEMA.COLUMNS` で実測しました。

| モデルの記述 | 生成された列型 |
| --- | --- |
| `public Point? Location { get; set; }` | `geography` |
| `.Property(z => z.Shape).HasColumnType("geometry")` | `geometry` |

`geography` を選んだ場合、SQL Server は多角形の頂点の並び順に制約を課します。公式ドキュメントは**外周は反時計回り、内側の穴は時計回り**でなければならず、**NTS がデータベースに送る前に検証する**と説明しています。実際に時計回りの多角形を保存しようとすると、次の例外になりました（実測で確認）。`geometry` 列では同じ多角形が問題なく保存できます。

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

`IsDescendantOf` や `GetLevel` はそのまま SQL Server の T-SQL のメソッド呼び出しに翻訳されます（実測）。

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

結果は「開発部, 第一課」でした。**`IsDescendantOf` は自分自身も配下として含みます。** 公式ドキュメントもこの挙動を明記しており、サンプルクエリでは自分自身を明示的に除外しています。「開発部より下だけ」がほしいなら、比較を 1 つ足します。

```csharp
var strictly = await db.Nodes
    .Where(n => n.Path.IsDescendantOf(devPath) && n.Path != devPath)
    .ToListAsync();
```

```sql
-- SQL Server
WHERE [n].[Path].IsDescendantOf(@devPath) = CAST(1 AS bit) AND [n].[Path] <> @devPath
```

こちらの結果は「第一課」だけになりました（実測）。`GetLevel()` は深さを返し、実測では 全社=0 / 開発部=1 / 第一課=2 / 営業部=1 となりました。

> [!NOTE]
> `HierarchyId` 型は `Microsoft.EntityFrameworkCore.SqlServer.Abstractions` パッケージで定義されており、こちらは他のパッケージへの参照を持ちません。エンティティを定義するプロジェクトだけが `Abstractions` を参照し、実際にクエリを実行するプロジェクトが `HierarchyId` パッケージを参照する、という分け方ができます。

### SQL Server 固有の列オプション

#### スパース列

**スパース列 (sparse column)** は、`NULL` の格納を最適化する代わりに、`NULL` でない値の取得コストが上がる列です。TPH 継承（[付録1の「継承のマッピング」](/appendix-efcore-01/#継承のマッピング)）のように「一部の型にしか存在しない列」がテーブルの大半で `NULL` になるケースで効きます。

```csharp
modelBuilder.Entity<SpecialPost>()
    .Property(x => x.Extra)
    .IsSparse();
```

#### UTF-8 の照合順序

SQL Server 2019 以降は `char` / `varchar` 列に UTF-8 の照合順序を指定でき、Unicode を `nvarchar` より小さく格納できる場合があります。EF Core からは、列の型を `varchar` にしたうえで `_UTF8` で終わる照合順序を指定し、あわせて `IsUnicode()` を呼びます。照合順序がクエリの結果そのものを変える点は[付録3の「大文字小文字の区別は照合順序が決める」](/appendix-efcore-03/#大文字小文字の区別は照合順序が決める)で扱います。

```csharp
modelBuilder.Entity<SpecialPost>()
    .Property(b => b.Name)
    .HasColumnType("varchar(max)")
    .UseCollation("LATIN1_GENERAL_100_CI_AS_SC_UTF8")
    .IsUnicode();
```

この 2 つを組み合わせると、次の DDL が生成されました（実測）。

```sql
CREATE TABLE [Posts] (
    [Id] int NOT NULL IDENTITY,
    [Name] varchar(max) COLLATE LATIN1_GENERAL_100_CI_AS_SC_UTF8 NOT NULL,
    [Extra] nvarchar(max) SPARSE NULL,
    CONSTRAINT [PK_Posts] PRIMARY KEY ([Id])
);
```

作成後に `sys.columns` を確認すると、`Extra` は `is_sparse=True`、`Name` は `varchar` 型で照合順序が `Latin1_General_100_CI_AS_SC_UTF8` になっていました。`varchar` 列に日本語を保存して読み戻す往復も実測で成功しています。

#### メモリ最適化テーブル

テーブル全体をメモリに常駐させる **メモリ最適化テーブル (memory-optimized table)** も、モデル側から指定できます。

```csharp
modelBuilder.Entity<MemItem>().ToTable(t => t.IsMemoryOptimized());
```

SQL Server で生成される DDL は、メモリ最適化データ用のファイルグループを用意する長いスクリプトに続いて、次のテーブル定義になります（実測）。主キーが自動的に **非クラスター化** になる点に注意してください。

```sql
CREATE TABLE [Items] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_Items] PRIMARY KEY NONCLUSTERED ([Id])
) WITH (MEMORY_OPTIMIZED = ON);
```

ファイルグループを追加するスクリプトは `SERVERPROPERTY('IsXTPSupported') = 1` で保護されているため、メモリ最適化に対応していないエディションでは何も実行されません。

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

第 2 引数 `stored` を省略すると **仮想 (virtual) 計算列** になり、値は取得のたびに計算されます。`stored: true` を指定すると **格納 (stored / persisted) 計算列** になり、行の更新のたびに計算されて他の列と同じようにディスクに保存されます。SQL Server 2022 に対して生成された DDL は次のとおりです（実測）。

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

`FirstName = "Taro"` / `LastName = "Yamada"` を保存して読み直すと、`DisplayName` は `Taro Yamada`、`PersistedName` は `Yamada, Taro` になりました（実測）。

> [!WARNING]
> 計算列のプロパティに C# 側で値を代入しても、その値はデータベースに書き込まれません。実測では例外も発生せず `SaveChanges` が成功し、値は無視されました。公式ドキュメントも「既定値の代わりに明示的な値を指定することはできるが、計算列に対して同じことはできない」と述べています。**アプリケーションから書き換えたい値には計算列を使わないでください。**

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

実際に Azure SQL Database（サーバーレスの `GP_S_Gen5_1`、最大サイズ 1 GB で作成）に対して `dotnet ef database update` を実行したところ、適用後に最大サイズが **10 GB** に変わっていることを確認しました（実測）。

> [!WARNING]
> `ALTER DATABASE` はトランザクションの中で実行できません。実測では適用時に次の警告が出ました。
>
> ```text
> The migration operation 'BEGIN DECLARE @db_name ...' from migration 'Initial' cannot be
> executed in a transaction. If the app is terminated or an unrecoverable error occurs while
> this operation is being executed then the migration will be left in a partially applied
> state and would need to be reverted manually before it can be applied again.
> Create a separate migration that contains just this operation.
> ```
>
> 警告が案内しているとおり、**この操作だけを含む単独のマイグレーションに分けて**ください。テーブル作成と混ぜると、途中で失敗したときに手作業での巻き戻しが必要になります。

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

この差は生成される SQL を変えます。`Math.Max` / `Math.Min` は互換性レベル 160 以上で `GREATEST` / `LEAST` に翻訳されますが、**それ未満では翻訳自体ができず例外になります。** SQL Server 2022（データベース側の互換性レベルは 160）に対して、EF Core 側の設定だけを変えて実測した結果です。

```csharp
// Rating と Rating2 の大きいほうが 4 を超えるブログ
var blogs = await db.Blogs.Where(b => Math.Max(b.Rating, b.Rating2) > 4).ToListAsync();
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
> **SQL Server 2022 を使っていても、EF Core 側で互換性レベルを上げなければ新しい関数は使われません。** 上の例では、データベース側の `compatibility_level` が 160 であるにもかかわらず、EF Core が既定の 150 で動作したため翻訳に失敗しました。逆に、**データベースが古いのに EF Core 側だけ高いレベルを構成すると、生成された SQL をデータベースが実行できません。** 公式も「これは EF 自身の構成であり、実際のデータベースの互換性レベルには影響しない」と明記しています。両者を揃えてください。現在の値は次の SQL で確認できます。
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

EF Core の `CommandTimeout` は **既定では未設定 (`null`)** です。実測でも `db.Database.GetCommandTimeout()` は `null` を返しました。この場合は ADO.NET プロバイダーの既定値が使われ、`Microsoft.Data.SqlClient` の `SqlCommand.CommandTimeout` は公式ドキュメントに「既定は 30 秒」と記載されています。ログに `CommandTimeout='30'` と出るのはこのためです。

設定方法は 2 つあります。

```csharp
// 1. DbContext 全体の既定として構成する
builder.Services.AddDbContext<BloggingContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions => sqlOptions.CommandTimeout(120)));

// 2. 特定の処理だけ実行時に変更する
context.Database.SetCommandTimeout(180);
```

実測では、`CommandTimeout` を 3 秒に設定して 10 秒かかるコマンドを実行すると、約 3.2 秒で `SqlException` が発生しました。エラー番号は `-2`（タイムアウト）です。

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
> Ruby on Rails や Django のように、モデル定義側で列長を宣言する ORM に慣れていると「毎回 `HasMaxLength` を書くのか」と感じるかもしれません。EF Core では `ConfigureConventions` がその役割を担い、プロジェクト全体の既定値をコード 1 か所で決められます。

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

```csharp
modelBuilder.Entity<Blog>()
    .HasQueryFilter("SoftDeletionFilter", b => !b.IsDeleted)
    .HasQueryFilter("TenantFilter", b => b.TenantId == tenantId);
```

名前を付けておくと、無効化したいフィルターだけを個別に選べます。

```csharp
// 論理削除のフィルターだけを無効化し、テナントのフィルターは維持する
var allBlogs = await context.Blogs
    .IgnoreQueryFilters(["SoftDeletionFilter"])
    .ToListAsync(cancellationToken);
```

`DbSet.Remove` を呼んだときに実際の削除ではなく `IsDeleted` を立てたい場合は、`SaveChangesAsync` をオーバーライドします。

```csharp
public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
{
    ChangeTracker.DetectChanges();

    foreach (var entry in ChangeTracker.Entries<Blog>().Where(e => e.State == EntityState.Deleted))
    {
        entry.State = EntityState.Modified;
        entry.CurrentValues[nameof(Blog.IsDeleted)] = true;
    }

    return await base.SaveChangesAsync(cancellationToken);
}
```

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

この警告を無視すると、**`Include` を足しただけで件数が減ります。** `fish` を含む `Blog` と含まない `Blog` に投稿を 3 件ずつ用意し、SQL Server 2022 で実測しました。

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
> `Blog` と `Post` の両方に `IsDeleted` のフィルターを設定したうえで、外部キーが既定の `ON DELETE CASCADE` のまま親を `Remove` してしまうと、SQL Server 2022 では次のようになりました（実測で確認）。
>
> | 操作 | `Blogs` の実行数 | `Posts` の実行数 |
> | --- | --- | --- |
> | `IsDeleted = true` にして保存（論理削除） | 1 | 2 |
> | 親を `Remove` して保存（物理削除） | **0** | **0** |
>
> 論理削除の運用に 1 か所でも物理削除の経路が混ざると、データベース側のカスケードによって子まで消えます。論理削除を使う場合は `OnDelete(DeleteBehavior.Restrict)` などでデータベースのカスケードを外し、削除は必ずフラグの更新として行ってください。

> [!NOTE]
> **Django** の `Manager` によるデフォルトクエリセットの絞り込みや、**Laravel** の Eloquent におけるグローバルスコープが同種の機能に相当します。**Hibernate** では、常に適用される静的な制約が `@SQLRestriction`（`@Where` は Hibernate 6.3 で非推奨になり、Hibernate 7 で削除されました）、セッション単位で有効・無効を切り替えられる動的なフィルターが `@FilterDef` / `@Filter` と、2 つの仕組みに分かれています。EF Core 10 の名前付きフィルターは、後者の `@FilterDef` / `@Filter` に近い粒度の制御を提供します。

---

### コード分析ルールがエンティティ定義と衝突する

プロジェクトで `<AnalysisLevel>latest-all</AnalysisLevel>` のように厳しいコード分析を有効にすると、上のエンティティ定義に対して次のような警告が出ます（実測で確認）。

| ルール | 内容 |
| --- | --- |
| `CA1002` | `List<Post>` ではなく `Collection<T>` を公開すべき |
| `CA2227` | コレクションプロパティのセッターを削除して読み取り専用にすべき |
| `CA1056` | `Url` プロパティは `string` ではなく `Uri` にすべき |

これらは汎用のライブラリ設計を想定したルールであり、**EF Core のエンティティには当てはまりません。** EF Core はコレクションナビゲーションの設定やリレーションシップの修正のためにセッターを利用しますし、`Uri` 型は標準では列にマッピングされません。エンティティを置いたフォルダーに対して、`.editorconfig` でこれらのルールを無効化するのが実務上の対応です。

```ini
# プロジェクト直下の Models フォルダーを対象にする場合
[Models/*.cs]
dotnet_diagnostic.CA1002.severity = none
dotnet_diagnostic.CA2227.severity = none
dotnet_diagnostic.CA1056.severity = none
```

パスは **`.editorconfig` を置いた場所からの相対パス** で解釈されます。`Models` がプロジェクト直下にあるとき、`[**/Models/*.cs]` と書くと**マッチせず抑制されません**（実測で確認）。意図したファイルに効いているかどうかは、ビルドして警告が消えることで必ず確かめてください。

## 5. 参考ドキュメント

- [外部キーと主キー | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/modeling/relationships/foreign-and-principal-keys)
- [キー | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/modeling/keys)
- [キーなしエンティティ型 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/modeling/keyless-entity-types)
- [シャドウプロパティとインジケータープロパティ | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/modeling/shadow-properties)
- [バッキングフィールド | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/modeling/backing-field)
- [シーケンス | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/modeling/sequences)
- [生成されるプロパティ値 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/modeling/generated-properties)
- [SQL Server / Azure SQL のテンポラルテーブル | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/temporal-tables)
- [SQL Server プロバイダー固有の列機能 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/columns)
- [SQL Server の HierarchyId | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/hierarchyid)
- [SQL Server のメモリ最適化テーブルのサポート | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/memory-optimized-tables)
- [SQL Server プロバイダーの値生成 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/value-generation)
- [空間データ | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/modeling/spatial)
- [SQL Server プロバイダーの空間データ | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/spatial)
- [一括構成 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/modeling/bulk-configuration)
- [グローバルクエリフィルター | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/querying/filters)
- [SQL Server プロバイダーのインデックス | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/indexes)
