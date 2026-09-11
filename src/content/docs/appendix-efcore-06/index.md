---
title: "付録 EF Core 6：テスト"
description: "EF Core を使うコードのテスト戦略、実データベースに対するテスト、SQLite インメモリの活用と制限、WebApplicationFactory による統合テスト、リポジトリパターンとモックを解説します。第8章の付録です。"
---

このページは [第8章：データベースアクセスと ORM (Entity Framework Core)](../08-entity-framework-core/index.md) の付録です。EF Core を使うコードのテストを扱います。

本編を先に読んでから、必要な項目をここで参照してください。

**第8章のほかの付録**

- [付録 EF Core 1：モデル定義（エンティティとリレーションシップ）](../appendix-efcore-01/index.md)
- [付録 EF Core 2：モデル定義（キー・採番・SQL Server 固有）](../appendix-efcore-02/index.md)
- [付録 EF Core 3：マイグレーションの詳細とクエリ](../appendix-efcore-03/index.md)
- [付録 EF Core 4：更新・トランザクション・レプリカ](../appendix-efcore-04/index.md)
- [付録 EF Core 5：パフォーマンス](../appendix-efcore-05/index.md)

---

## 目次

1. [データベースを使ったテスト](#1-データベースを使ったテスト)
   - [実データベースに対するテスト](#実データベースに対するテスト)
   - [製品コードがトランザクションを使うテスト](#製品コードがトランザクションを使うテスト)
   - [SQLite インメモリを使ったテスト](#sqlite-インメモリを使ったテスト)
   - [SQLite プロバイダーの制限を把握する](#sqlite-プロバイダーの制限を把握する)
   - [WebApplicationFactory で API ごとテストする](#webapplicationfactory-で-api-ごとテストする)
   - [リポジトリパターンとモック](#リポジトリパターンとモック)
2. [参考ドキュメント](#2-参考ドキュメント)

---

> [!NOTE]
> **実測値は、公式の説明を補足する条件付きの観測です。** 以下の数値は次の環境での結果で、仕様や推奨の根拠ではありません。所要時間だけでなく、**速さの順位や倍率もハードウェア・データ量・ネットワークに依存します**。各節の公式説明と測定条件を読み、自分のアプリケーションで評価してください。
>
> | 項目 | 値 |
> | --- | --- |
> | マシン | Apple M1 Max（10 コア）、macOS 26.6 |
> | .NET SDK | 10.0.400 |
> | EF Core | 10.0.11 |
> | SQLite | ローカルファイル（同一マシン） |
> | SQL Server | SQL Server 2022（Linux）、2 vCPU / 4 GB、Azure 上のリモート接続 |

## 1. データベースを使ったテスト

### 実データベースに対するテスト

xUnit では、テスト用のセットアップに **フィクスチャ** を使います。次は、クラスフィクスチャのインスタンスはテストクラスごとに生成し、同じテストプロセス内でデータベースの初期化を 1 回だけ行う例です。**本編で `BloggingContext` 用のマイグレーションを作成済みで、それを適用できる構成を前提とします。** `Migrate()` はモデルから新しいマイグレーションを生成する処理ではありません。

```csharp
public class TestDatabaseFixture
{
    private const string ConnectionString =
        @"Server=(localdb)\mssqllocaldb;Database=BloggingTest;Trusted_Connection=True";

    private static readonly object Lock = new();
    private static bool _databaseInitialized;

    public TestDatabaseFixture()
    {
        lock (Lock)
        {
            if (_databaseInitialized)
            {
                return;
            }

            using var context = CreateContext();
            context.Database.EnsureDeleted();
            context.Database.Migrate();
            SeedData(context);

            _databaseInitialized = true;
        }
    }

    public BloggingContext CreateContext()
        => new(new DbContextOptionsBuilder<BloggingContext>()
            .UseSqlServer(ConnectionString)
            .Options);

    private static void SeedData(BloggingContext context)
    {
        context.Blogs.AddRange(
            new Blog { Name = "Blog A", Url = "https://a.example.com" },
            new Blog { Name = "Blog B", Url = "https://b.example.com" });
        context.SaveChanges();
    }
}
```

> [!WARNING]
> [公式のテスト用データベースの案内](https://learn.microsoft.com/ja-jp/ef/core/testing/testing-with-the-database#setting-up-your-database-system)は、LocalDB より SQL Server Developer Edition の利用を一般に推奨しています。LocalDB は管理作業を抑えられる一方、次の制約があります。**`(localdb)\mssqllocaldb` は Windows 専用**です。
>
> - SQL Server Developer Edition がサポートする機能のすべてには対応していない
> - **Windows でしか利用できない**
> - サービスが起動するため、初回のテスト実行が遅れる
>
> macOS で LocalDB 接続文字列を使う最小 Context から `EnsureCreated()` を呼ぶ確認例では、次の例外を確認できています。上のフィクスチャ全体の実行結果ではありません。
>
> ```text
> System.PlatformNotSupportedException: LocalDB はこのプラットフォームでサポートされていません。
> ```
>
> SQL Server をコンテナーで使う場合は、公式の[対応ホスト・CPU の条件](https://learn.microsoft.com/ja-jp/sql/linux/install-upgrade/quickstart-install-docker?view=sql-server-ver16)も確認してください。Testcontainers や Docker を使うだけで、あらゆる OS・CPU 上でのサポートが保証されるわけではありません。

> [!NOTE]
> `IClassFixture<TestDatabaseFixture>` は、同じテストクラス内ではフィクスチャを共有しますが、**別のテストクラスとはインスタンスを共有しません**。ここでロックと静的フラグが共有するのは、データベースの作成とシードを 1 回だけ行うための初期化状態です。**テスト本体の実行を直列化したり、書き込みを分離したりする仕組みではありません。**
>
> [公式の初期化例](https://learn.microsoft.com/ja-jp/ef/core/testing/testing-with-the-database#creating-seeding-and-managing-a-test-database)もこの方式を使います。以下の読み取りテストと、[コミットせずロールバックする書き込みテスト](https://learn.microsoft.com/ja-jp/ef/core/testing/testing-with-the-database#tests-which-modify-data)を区別し、書き込み側で分離を保証する前提です。**実際に変更をコミットするテストを同じデータベースに混在させないでください。** その場合は[後述の別データベースと直列化・クリーンアップ](#製品コードがトランザクションを使うテスト)、またはテストクラスごとのデータベース分離が必要です。
>
> フィクスチャのインスタンス自体を複数クラスで共有するなら、xUnit の**コレクションフィクスチャ**を使います。ただし、同じコレクションのテストクラスは並列実行されません。別プロセスから同じデータベースを使う場合は、この静的フラグや xUnit のコレクションでは排他できないため、テスト実行ごとにデータベースを分けるなどの対策も必要です。

読み取りのテストはそのまま書けます。

```csharp
public class BlogQueryTests(TestDatabaseFixture fixture) : IClassFixture<TestDatabaseFixture>
{
    [Fact]
    public async Task Blogs_are_ordered_by_name()
    {
        await using var context = fixture.CreateContext();

        var blogs = await context.Blogs
            .AsNoTracking()
            .OrderBy(b => b.Name)
            .ToListAsync();

        Assert.Equal(["Blog A", "Blog B"], blogs.Select(b => b.Name));
    }
}
```

書き込みを伴うテストは、トランザクションを開始してコミットしないことで、そのトランザクションに参加する変更をロールバックできます。テストが別の接続でコミットした変更や外部への副作用まで取り消すものではありません。

```csharp
[Fact]
public async Task Can_add_blog()
{
    await using var context = fixture.CreateContext();
    await using var transaction = await context.Database
        .BeginTransactionAsync();

    context.Blogs.Add(new Blog { Name = "Blog C", Url = "https://c.example.com" });
    await context.SaveChangesAsync();

    var count = await context.Blogs.CountAsync();
    Assert.Equal(3, count);

    // コミットしないため、破棄時にロールバックされる
}
```

> [!TIP]
> CI 環境で本番と同じデータベースを用意するには、**Testcontainers** のようなライブラリで Docker コンテナーを起動する方法が便利です。テストの開始時にコンテナーを起動し、終了時に破棄することで、データベースの初期状態やバージョンをそろえやすくなります。ホストや CPU アーキテクチャなどへの依存までなくなるわけではありません。

### 製品コードがトランザクションを使うテスト

[公式のトランザクションを明示管理するテスト](https://learn.microsoft.com/ja-jp/ef/core/testing/testing-with-the-database#tests-which-explicitly-manage-transactions)では、データベースは通常入れ子のトランザクションをサポートしないため、**製品コードが明示的にトランザクションを管理する場合は、テストを外側のトランザクションで分離できない**と説明しています。

SQL Server 2022 で、テスト側のトランザクション内から `BeginTransactionAsync(IsolationLevel.Serializable)` を呼ぶ確認例では、次の例外を確認できています。

```text
InvalidOperationException: The connection is already in a transaction and cannot
participate in another transaction.
```

この種のテストは、公式によれば次のように扱う必要があります。

| 必要なこと | 理由 |
| --- | --- |
| テストごとにデータベースを元の状態へ戻す | 変更が実際にコミットされるため |
| 並列実行を無効にする | テスト同士が干渉するため |
| 他のテストとは**別のデータベース**を使うフィクスチャを用意する | 既存のテストに影響を与えないため |

xUnit では、複数のテストクラスでフィクスチャを共有する場合、クラスフィクスチャではなく**コレクションフィクスチャ**を使います。同じクラス内のテストは xUnit が並列化しないため、単一のクラスでしか使わないならクラスフィクスチャのままで構いません。

> [!TIP]
> 公式は「データベースを変更するテストクラスが複数あっても、**それぞれが自分のデータベースを参照する別々のフィクスチャ**を持てば並列実行できる。テスト用データベースをたくさん作って使うことは問題ではなく、役に立つならいつでもそうするべきだ」と述べています。並列実行をあきらめる前に、データベースを分けることを検討してください。

#### クリーンアップで全行の読み込みを避ける

コミットを伴うテストでは、テスト間に変更を残さないクリーンアップが必要です。[公式のテストガイド](https://learn.microsoft.com/ja-jp/ef/core/testing/testing-with-the-database#efficient-database-cleanup)は、読み込み後の `RemoveRange` による削除に対し、生の SQL の `DELETE` を選択肢として挙げています。[`ExecuteDelete` の公式説明](https://learn.microsoft.com/ja-jp/ef/core/saving/execute-insert-update-delete)も、エンティティを読み込まず削除する利点を示しています。生 SQL と EF Core API 全般の速さを比較する話ではありません。

次は、SQL Server 2022 で 1,000 行を削除する 2 方式の確認例です。

| 方法 | 所要時間 |
| --- | --- |
| `RemoveRange` + `SaveChangesAsync` | 443 ms |
| `ExecuteSqlRawAsync("DELETE FROM [Blogs]")` | 9 ms |

この条件の所要時間は 443 ms と 9 ms です。比較対象は全行を読み込む `RemoveRange` + `SaveChanges` と生 SQL で、`ExecuteDelete` の結果を含みません。比率や順位は他の条件へ一般化できません。

削除だけでは初期データまでなくなります。初期データを前提とするテストでは、コミットするテスト専用のフィクスチャにも前述の `SeedData` と同じ初期化処理を用意し、削除後に再投入してください。次のメソッドをそのフィクスチャに追加します。上の所要時間は削除処理だけのもので、再投入の時間は含みません。

```csharp
public async Task CleanupAsync()
{
    await using var context = CreateContext();
    await context.Database.ExecuteSqlRawAsync("DELETE FROM [Blogs]");
    SeedData(context);
}
```

> [!TIP]
> 公式ドキュメントは [respawn](https://github.com/jbogard/respawn) パッケージの利用も勧めています。データベースを効率的に空にするうえ、**消すテーブルを列挙する必要がない**ため、モデルにテーブルを追加してもクリーンアップのコードを更新せずに済みます。

> [!NOTE]
> テストのたびに `EnsureDeleted()` でデータベースを作り直すのが遅い場合、公式は「フィクスチャのコンストラクターから `EnsureDeleted` を**一時的に**コメントアウトしてデータベースを使い回してもよい」と案内しています。ただしモデルを変更するとスキーマが古いままになりテストが失敗するため、**開発サイクル中の一時的な措置に限る**とも明記されています。

#### テストで内部サービスを差し替えるとき

EF Core は、クエリのコンパイルやモデルの構築に使う**内部サービスプロバイダー**を、構成が同じ `DbContext` のあいだで再利用（キャッシュ）します。テストで `ReplaceService` を使ってサービスを差し替える場合、この共有が問題になることがあります。`EnableServiceProviderCaching(false)` を指定すると、内部サービスプロバイダーのキャッシュが無効になります。「テストごと」ではなく、Context が内部サービスを必要とする際の構成に関わる指定です。

```csharp
optionsBuilder
    .EnableServiceProviderCaching(false)
    .UseSqlServer(connectionString);
```

> [!WARNING]
> [公式 API](https://learn.microsoft.com/ja-jp/dotnet/api/microsoft.entityframeworkcore.dbcontextoptionsbuilder.enableserviceprovidercaching?view=efcore-10.0)は、キャッシュ無効化が性能を大きく損なうため、テストなどを除く多くの場合は既定の `true` を推奨しています。次は SQLite で Context の生成と単純なクエリを 100 回繰り返す条件の補足です。
>
> | 設定 | 所要時間 |
> | --- | --- |
> | `EnableServiceProviderCaching(true)`（既定） | 52 ms |
> | `EnableServiceProviderCaching(false)` | 439 ms |
>
> この条件の所要時間は 52 ms と 439 ms で、一般的な倍率ではありません。無効化は内部サービスを差し替えるテストなど、理由が明確な用途で検討してください。

---

### SQLite インメモリを使ったテスト

より軽量に済ませたい場合は、SQLite のインメモリモードが選択肢になります。リレーショナルデータベースとして動作するため、InMemory プロバイダーより挙動が本番に近くなります。

```csharp
public sealed class SqliteContextFactory : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteContextFactory()
    {
        // 接続を開いている間だけデータベースが存在する
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public BloggingContext CreateContext()
        => new(new DbContextOptionsBuilder<BloggingContext>()
            .UseSqlite(_connection)
            .Options);

    public void Dispose() => _connection.Dispose();
}
```

> [!IMPORTANT]
> SQLite のインメモリデータベースは、接続が閉じられた時点で破棄されます。そのため `SqliteConnection` をテストの寿命の間開いたまま保持し、複数の `DbContext` インスタンスで共有する必要があります。
>
> また、SQLite には SQL Server と異なる制約があります。特に次の点は実際に踏みやすいため注意してください。
>
> - [公式の SQLite クエリ制限](https://learn.microsoft.com/ja-jp/ef/core/providers/sqlite/limitations#query-limitations)では、**`DateTimeOffset` の読み書きと等価比較はサポートされますが、大小比較・並べ替えにはクライアント評価が必要**です。次の例外は EF Core 10.0.11 の確認結果で、操作ごとの例外型を固定する契約ではありません。
>
>   この確認例の `Where(e => e.At > pivot)` は `InvalidOperationException` です（読みやすく改行し、末尾の案内を省略した抜粋）。
>
>   ```text
>   System.InvalidOperationException: The LINQ expression 'DbSet<Ev>()
>       .Where(e => e.At > @pivot)' could not be translated. Either rewrite the query in a form
>   that can be translated, or switch to client evaluation explicitly by inserting a call to
>   'AsEnumerable', 'AsAsyncEnumerable', 'ToList', or 'ToListAsync'.
>   ```
>
>   同じ確認例の `OrderBy(e => e.At)` は `NotSupportedException` です（以下は抜粋）。
>
>   ```text
>   System.NotSupportedException: SQLite does not support expressions of type 'DateTimeOffset'
>   in ORDER BY clauses. Convert the values to a supported type, or use LINQ to Objects to order
>   the results on the client side.
>   ```
>
>   [公式のクライアント評価の説明](https://learn.microsoft.com/ja-jp/ef/core/querying/client-eval)では、最終投影以外の翻訳できない式を自動でクライアント評価せず、例外にします。必要なら取得後に明示的に評価できますが、実データベース側の比較動作を検証する代替にはなりません。等価比較まで一律に禁止する制限ではありません。
> - `rowversion` による同時実行トークンは SQL Server 固有の機能で、SQLite では自動的に更新されません。
> - `decimal` の精度、`ALTER TABLE` の対応範囲、スキーマ（名前空間）の扱いが異なります。
> - [公式の冪等スクリプトの制限](https://learn.microsoft.com/ja-jp/ef/core/providers/sqlite/limitations#idempotent-script-limitations)では、SQLite に必要な手続き言語がないため生成非対応です。この確認例でも `Generating idempotent scripts for migrations is not currently supported for SQLite.` を確認できています。
> - 規約の対象となる整数主キーでは、値の生成方法が異なります。SQL Server では `IDENTITY` 列になりますが、SQLite では規約により `AUTOINCREMENT` が付きます。
>
> SQL Server 固有の機能を使っている箇所は、実データベースに対する統合テストで確認してください。

> [!TIP]
> [公式の SQLite 値生成](https://learn.microsoft.com/ja-jp/ef/core/providers/sqlite/value-generation)では、EF Core 10 から `AUTOINCREMENT` を設定できると説明しています。規約では、値生成が構成され、値変換がなく、複合キーの一部でも外部キーでもない整数主キーに付きます。`AUTOINCREMENT` は CPU・メモリ・ディスクのオーバーヘッドと引き換えに、削除済みキーの再利用を防ぎます。値の再利用が問題にならない場合にだけ無効化を検討してください。
>
> 次は、この規約の対象となる `Blog` モデルの DDL 確認例です。
>
> ```sql
> -- 既定（規約）
> CREATE TABLE "Blogs" (
>     "Id" INTEGER NOT NULL CONSTRAINT "PK_Blogs" PRIMARY KEY AUTOINCREMENT,
>     "Name" TEXT NOT NULL
> );
>
> -- SetValueGenerationStrategy(SqliteValueGenerationStrategy.None) または ValueGeneratedNever()
> CREATE TABLE "Blogs" (
>     "Id" INTEGER NOT NULL CONSTRAINT "PK_Blogs" PRIMARY KEY,
>     "Name" TEXT NOT NULL
> );
> ```
>
> ```csharp
> protected override void OnModelCreating(ModelBuilder modelBuilder)
> {
>     modelBuilder.Entity<Blog>()
>         .Property(b => b.Id)
>         .Metadata.SetValueGenerationStrategy(
>             Microsoft.EntityFrameworkCore.Metadata.SqliteValueGenerationStrategy.None);
> }
> ```
>
> 逆に、値変換を挟んでいるなどの理由で規約が働かない場合は、`UseAutoincrement()` で明示的に有効化できます。なお `ValueGeneratedNever()` を使う場合は、EF Core で保存する前にアプリケーション側が値を用意する必要があります。ただし、公式の説明にあるとおり、この指定はデータベース側の既定の値生成までは止めません。前掲の `INTEGER PRIMARY KEY` の表を新規作成した場合や、マイグレーションでこの定義に再構築した場合も、SQLite の既定の **ROWID による採番**は残ります。そのため、EF Core を経由せず `Id` を省略して挿入すれば値が生成されます。**`AUTOINCREMENT` を外すことと、データベース側の採番を完全に無効にすることは別**です。

> [!WARNING]
> [EF Core 10 の破壊的変更](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-10.0/breaking-changes#using-getdatetimeoffset-without-an-offset-now-assumes-utc)では、SQLite のタイムゾーンの扱いが重大度「高」に分類されています。オフセットなしのテキスト（例: `2026-08-31 12:00:00`）を `DateTimeOffset` として読むと、Microsoft.Data.Sqlite 10.0 からは **UTC** と解釈します。以前は **ローカルタイムゾーン** の値とみなしていました。
>
> 次は日本時間（UTC+9）、Microsoft.Data.Sqlite 10.0.11 で、既定と互換スイッチ有効の別プロセスからオフセットなしの値を読む確認例です。
>
> ```text
> EF Core 10 の既定 : 2026-08-31T12:00:00+00:00 （UTC 12:00 とみなす）
> EF Core 10 で互換スイッチを有効: 2026-08-31T12:00:00+09:00 （UTC 03:00 とみなす）
> ```
>
> EF Core が書き込んだ値にはオフセットが付くため往復は一致しますが、**他のシステムや旧バージョンが書き込んだオフセットなしのデータを読む場合は結果が変わります。** すぐに修正できない場合の一時的な回避策として、次の `AppContext` スイッチで従来の挙動に戻せます（公式は「最後の手段」と位置づけています）。
>
> ```csharp
> // SQLite を初めて使う前に、アプリケーションの起動処理で設定する
> AppContext.SetSwitch("Microsoft.Data.Sqlite.Pre10TimeZoneHandling", isEnabled: true);
> ```
>
> [Microsoft.Data.Sqlite 10.0.11 の公式実装](https://github.com/dotnet/efcore/blob/v10.0.11/src/Microsoft.Data.Sqlite.Core/SqliteValueReader.cs#L14-L15)では、スイッチを読み取り処理の型初期化時に取得します。同版の確認例も、初回読み取り後の設定は UTC のまま、別プロセスの初回操作前の設定は `+09:00` です。これは同版の実装に対応する補足です。
>
> 同じ公式の SQLite 破壊的変更には、次の 2 点も重大度「高」として掲載されています。表の右列は Microsoft.Data.Sqlite 10.0.11、日本時間（UTC+9）での確認例です。
>
> | 変更点 | 実測した挙動 |
> | --- | --- |
> | パラメーターに `SqliteType.Real` を指定して `DateTimeOffset` を書き込むと UTC で保存される | `2026-08-31 12:00+09:00` を書くとユリウス日 `2461283.625`（= UTC 03:00）が格納され、読み戻すと `2026-08-31T03:00:00+00:00` になる。**元のオフセット `+09:00` は失われる** |
> | オフセット付きの値を `GetDateTime` で読むと UTC で返る | `2026-08-31 12:00:00+09:00` を `GetDateTime` で読むと `2026-08-31T03:00:00Z`（`Kind` は `Utc`）が返る |
>
> オフセット自体を保持したい用途では、保存形式と読み取り API を確認してください。UTC の `DateTime` に統一して表示時にタイムゾーンを適用する設計は公式の推奨ですが、それだけですべての既存データや API の差異がなくなるわけではありません。特にオフセットなしの既存値は、その値が表す時刻を決めてから移行します。

### SQLite プロバイダーの制限を把握する

**本番が SQL Server なら SQLite のテスト成功は本番の保証ではありません。** [公式の SQLite 制限](https://learn.microsoft.com/ja-jp/ef/core/providers/sqlite/limitations)は、スキーマ・シーケンス・データベース生成の同時実行トークンの非対応を説明しています。一方、`decimal` の現行の翻訳は[公式の関数マッピング](https://learn.microsoft.com/ja-jp/ef/core/providers/sqlite/functions#numeric-functions)と [EF Core 10 の新機能](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-10.0/whatsnew#other-query-improvements)も参照してください。次は EF Core 10.0.11 / SQLite での確認結果です。例外の型や DDL の形はこの構成での観測です。

| 検証した内容 | 結果 |
| --- | --- |
| `decimal` 列の型 | `TEXT` にマップされる |
| `decimal` の等価比較 | `WHERE "Price" = '10.5'` としてそのまま翻訳される |
| `decimal` の大小比較 | `WHERE ef_compare("Price", '5.0') > 0` に翻訳される |
| `decimal` の並べ替え | `ORDER BY "Price" COLLATE EF_DECIMAL` に翻訳される |
| `DateTimeOffset` の大小比較 | `InvalidOperationException`（翻訳できない） |
| `TimeSpan` の大小比較 | `InvalidOperationException`（翻訳できない） |
| `ToTable("Items", "sales")` | スキーマが**無視され**て `CREATE TABLE "Items"` になる |
| `HasSequence<int>()` | `NotSupportedException: SQLite does not support sequences.` |
| `IsRowVersion()` | 例外にならず `"RowVersion" BLOB NOT NULL` が作られる |

特に、スキーマ指定と `IsRowVersion()` は、**テーブルを作成できたことだけでは本番と同じ動作を確認できません。** `HasSequence<int>()` は非対応の例外になるため、区別してください。

- スキーマ指定は警告が出たうえで無視されるので、SQL Server で `sales.Items` と `dbo.Items` を使い分けている設計は SQLite 上では区別されません。
- SQLite は SQL Server の `rowversion` のようにトークンを自動生成・更新しません。**SQL Server の自動生成まで含む同時実行制御の代用にはなりません。** この確認例の `BLOB NOT NULL` 列に既定値・トリガーなしで挿入する条件では、`NOT NULL constraint failed` を内部例外とする `DbUpdateException` を確認できています。

[公式のアプリケーション管理トークン](https://learn.microsoft.com/ja-jp/ef/core/saving/concurrency#application-managed-concurrency-tokens)は、自動更新型を持たない SQLite でも利用できると説明しています。次は `Guid` に `IsConcurrencyToken()` を設定した確認例です（EF Core 10.0.11、SQLite インメモリ）。更新時のトークン再生成の有無を分けています。

| 先に保存する側の処理 | 古いトークンを持つ側の処理 | 結果 |
| --- | --- | --- |
| データとトークンを更新 | 更新して保存 | `DbUpdateConcurrencyException` |
| データとトークンを更新 | 削除して保存 | `DbUpdateConcurrencyException` |
| データだけ更新し、トークンは変更しない | 更新して保存 | 競合を検出せず、後の値で上書き |

表は、2 つの `DbContext` で同じ行を読み、先行更新の後に古いトークンで保存する確認例です。**トークンの再生成はアプリケーションの責任です。** 本番の `rowversion` の自動更新は SQL Server で検証してください。設定と競合解決は[付録4の「楽観的同時実行制御」](../appendix-efcore-04/index.md#楽観的同時実行制御)で扱います。

> [!NOTE]
> [EF Core 10 の新機能](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-10.0/whatsnew#other-query-improvements)が挙げる改善は、SQLite の `decimal` に対する `MAX` / `MIN` / `ORDER BY` の対応です。大小比較の `ef_compare()` は[関数マッピング](https://learn.microsoft.com/ja-jp/ef/core/providers/sqlite/functions#numeric-functions)に記載されていますが、EF Core 10 で初めて追加されたものとはしません。[10.0.11 の接続実装](https://github.com/dotnet/efcore/blob/v10.0.11/src/EFCore.Sqlite.Core/Storage/Internal/SqliteRelationalConnection.cs)では `EF_DECIMAL` を登録しており、表の並べ替えも同版で確認できています。SQLite の `TEXT` 格納と SQL Server の固定精度・小数桁数を持つ列は別の型システムです。

#### Math のメソッドはサーバー側で評価される

[公式の EF Core 8 の破壊的変更](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-8.0/breaking-changes)では、SQLite に対応する数学関数を持つ `Math` メソッドの翻訳が拡張されたと説明しています。EF Core 7 までは `Abs` / `Max` / `Min` / `Round` だけが翻訳され、それ以外は最終投影でクライアント評価されていました。現行の対応は[公式の関数マッピング](https://learn.microsoft.com/ja-jp/ef/core/providers/sqlite/functions#numeric-functions)を参照してください。

```csharp
var q = db.Ms.Select(x => new
{
    P = Math.Pow(x.V, 2),
    Sq = Math.Sqrt(x.V),
    L = Math.Log(x.V),
    E = Math.Exp(x.V),
});
```

この確認例では、4 つとも次の SQLite 関数への翻訳を確認できています。`Math.Log` は `ln` に対応します。

```sql
SELECT pow("m"."V", 2.0) AS "P", sqrt("m"."V") AS "Sq",
       ln("m"."V") AS "L", exp("m"."V") AS "E"
FROM "Ms" AS "m"
```

> [!NOTE]
> SQLite の数学関数はバージョン 3.35.0 で追加されましたが、**ビルド時に既定では無効**です。EF Core の SQLite プロバイダーが依存する `SQLitePCLRaw.bundle_e_sqlite3`（および `SQLitePCLRaw.bundle_e_sqlcipher`）では有効化されているため、通常の使い方であれば影響はありません。
>
> 一方、別の方法でネイティブの SQLite ライブラリを組み込んでいる場合は、数学関数が無効なまま SQL が発行され、実行時に *no such function* エラーになる可能性があります。その場合は `SQLITE_ENABLE_MATH_FUNCTIONS` を有効にしてビルドするか、`Microsoft.Data.Sqlite` の `CreateFunction` で自分で関数を登録します。

#### Unhex は null を返すことがある

[公式の EF Core 9 の破壊的変更](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes)は、SQLite の `unhex` が不正な入力に `NULL` を返すため、`EF.Functions.Unhex()` の戻り値注釈を `byte[]?` に修正したと説明しています。**16 進数の変換結果が `null` になる場合を考慮してください。**

```csharp
var q = db.Hexes.Select(x => EF.Functions.Unhex(x.S));
// SELECT unhex("h"."S") FROM "Hexes" AS "h"
```

次は `48656C6C6F`（`Hello`）と不正な 16 進文字列の 2 行を使う確認例です。後者の `null` を確認できています。

```text
結果: Hello
結果: null
```

EF Core 9 以降の宣言は `byte[]?` です。以前の `byte[]` という注釈と実際の `null` の可能性の食い違いを直す変更であり、観測結果から戻り値の契約を決めるものではありません。

```csharp
// 正しい 16 進数だと確信できる場合
var data = await db.Hexes.Select(b => EF.Functions.Unhex(b.S)!).ToListAsync();

// そうでない場合は null チェックを入れる
```

#### 先行書き込みログ (WAL) が有効かを確認する

[公式の WAL の案内](https://learn.microsoft.com/ja-jp/dotnet/standard/data/sqlite/async)では、EF Core が作成するデータベースは既定で WAL 有効と説明されています。**ここで対象にするのはファイルデータベースです。** [SQLite 自体の公式仕様](https://www.sqlite.org/pragma.html#pragma_journal_mode)では、インメモリデータベースのジャーナルモードは `MEMORY` または `OFF` に限られ、WAL には切り替えられません。[エラー処理の説明](https://learn.microsoft.com/ja-jp/dotnet/standard/data/sqlite/database-errors#locking-retries-and-timeouts)は、Microsoft.Data.Sqlite が busy / locked エラーを成功またはタイムアウトまで再試行するとしています。これは EF Core の実行戦略とは別の層です。

補足として、EF Core 10.0.11 / SQLite、WAL 無効、別接続の書き込みロックを 300 ミリ秒後に解除する条件では、`RETURNING` 付きの保存成功を確認できています。EF の実行戦略は再試行無効です。この結果をすべてのロック競合の回復保証や、WAL 不要の根拠にはしません。

次は作成方法ごとの `journal_mode` の確認例です。インメモリの行は EF Core 10.0.11 / `Data Source=:memory:` での確認で、`PRAGMA journal_mode = 'wal'` を実行しても `memory` のままです。

| 作成方法 | `PRAGMA journal_mode` |
| --- | --- |
| EF Core が作成したファイルデータベース | `wal` |
| `SqliteConnection` で直接作成したファイルデータベース | `delete` |
| EF Core が作成したインメモリデータベース | `memory` |

既存のファイルを引き継ぐ場合は、作成経路だけで設定を仮定せず、`journal_mode` を確認してください。WAL を有効にするには、公式が示す次の `PRAGMA` を使います。

```sql
PRAGMA journal_mode = 'wal';
```

#### マイグレーションの制限

[公式の SQLite マイグレーション制限](https://learn.microsoft.com/ja-jp/ef/core/providers/sqlite/limitations#migrations-limitations)は、対応する一部の変更を**テーブルの再構築**で行うと説明しています。次は `Note` 列を削除して `Rank` 列を追加するマイグレーションの SQL から、テーブルの変更部分を抜粋した確認例です。マイグレーション履歴への記録などは省略しています。

```sql
BEGIN TRANSACTION;
ALTER TABLE "Blogs" ADD "Rank" INTEGER NOT NULL DEFAULT 0;
CREATE TABLE "ef_temp_Blogs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Blogs" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Rank" INTEGER NOT NULL
);
INSERT INTO "ef_temp_Blogs" ("Id", "Name", "Rank")
SELECT "Id", "Name", "Rank" FROM "Blogs";
COMMIT;

PRAGMA foreign_keys = 0;
BEGIN TRANSACTION;
DROP TABLE "Blogs";
ALTER TABLE "ef_temp_Blogs" RENAME TO "Blogs";
COMMIT;
PRAGMA foreign_keys = 1;
```

この変更に対して生成されたスクリプトには、全行のコピーと外部キー検査の一時停止が含まれます。すべての列削除で必ず同じスクリプトになるという意味ではありません。行数の多いテーブルでは所要時間と一時的なディスク使用量に注意してください。

また、[公式の冪等スクリプトの制限](https://learn.microsoft.com/ja-jp/ef/core/providers/sqlite/limitations#idempotent-script-limitations)のとおり、SQLite は必要な条件分岐の手続き言語を持たず、冪等スクリプトを生成できません。次は[本編の生成コマンド](../08-entity-framework-core/index.md#sql-スクリプトとマイグレーションバンドル)に対応するエラー確認例で、末尾の案内を省略した抜粋です。

```text
Generating idempotent scripts for migrations is not currently supported for SQLite.
```

> [!WARNING]
> [公式の DbContext のスレッド制約](https://learn.microsoft.com/ja-jp/ef/core/dbcontext-configuration/#avoiding-dbcontext-threading-issues)は、同じインスタンスの並行操作を禁止し、未検出でも未定義動作やデータ破損があり得るとしています。[SQLite の非同期 API は同期的に動作する](https://learn.microsoft.com/ja-jp/dotnet/standard/data/sqlite/async)ため、`Task.WhenAll` を書いても操作が重なるとは限りません。最小モデルで 4 本の取得タスクを順に作成し、その後 `Task.WhenAll` を待つ 20 回の確認では例外なしですが、同時開始を保証した試験ではありません。
>
> **`Task.Run` を使うこと自体も、例外の発生条件ではありません。** EF Core 10.0.11 の確認例では、各呼び出しの直後に `await` する条件は成功、意図的にクエリを重ねる条件は `InvalidOperationException` です。[同時実行検出](../appendix-efcore-05/index.md#同時実行検出を無効にしてはいけない)の有無を安全性の判定には使わないでください。
>
> つまり **SQLite を使った単体テストでは問題が表面化せず、本番の SQL Server で初めて落ちる**、ということが起こり得ます。「テストが通ったから安全」と考えず、1 つの `DbContext` インスタンスを複数の処理で並行利用しない設計を徹底してください。

> [!TIP]
> [公式のテスト戦略](https://learn.microsoft.com/ja-jp/ef/core/testing/choosing-a-testing-strategy)に従い、本番と同じデータベースに対するテストを基本に検討してください。SQLite を代用する場合は、そのテストで確認できる範囲を限定します。Testcontainers などで実データベースを用意する場合も、対応するホスト・CPU の条件を確認してください。

### WebApplicationFactory で API ごとテストする

ASP.NET Core の統合テストでは、`WebApplicationFactory<TEntryPoint>` でアプリケーション全体を起動し、テスト用のデータベースに差し替えます。

```csharp
using System.Data.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public class BloggingApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // 既定の DbContext 構成を取り除く
            // IDbContextOptionsConfiguration<T> まで消さないとプロバイダーが重複する
            services.RemoveAll<DbContextOptions<BloggingContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<IDbContextOptionsConfiguration<BloggingContext>>();

            // 接続を開いたままにしてインメモリデータベースを保持する
            services.AddSingleton<DbConnection>(_ =>
            {
                var connection = new SqliteConnection("DataSource=:memory:");
                connection.Open();
                return connection;
            });

            services.AddDbContext<BloggingContext>((serviceProvider, options) =>
            {
                var connection = serviceProvider.GetRequiredService<DbConnection>();
                options.UseSqlite(connection);
            });
        });

        builder.UseEnvironment("Testing");
    }
}
```

テスト側では `HttpClient` を取得してエンドポイントを呼び出します。

```csharp
public class BlogsApiTests(BloggingApiFactory factory) : IClassFixture<BloggingApiFactory>
{
    [Fact]
    public async Task Get_blogs_returns_ok()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BloggingContext>();
        await context.Database.EnsureCreatedAsync();

        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/blogs");

        response.EnsureSuccessStatusCode();
    }
}
```

> [!WARNING]
> [公式の構成の説明](https://learn.microsoft.com/ja-jp/ef/core/dbcontext-configuration/#configuredbcontext-and-adddbcontext-precedence)は、別プロバイダーを構成しても以前の構成は削除されず、完全な置換には登録の除去・再登録などが必要だと注意しています。[公式の統合テスト例](https://learn.microsoft.com/ja-jp/aspnet/core/test/integration-tests?view=aspnetcore-10.0#customize-webapplicationfactory)も `IDbContextOptionsConfiguration<TContext>` の登録を除去しています。**上の例でも `DbContextOptions` だけでなく、この構成登録を取り除きます。**
>
> この構成登録を残して両プロバイダーを混在させた確認例では、DI 解決は成功しても `context.Model` へのアクセスで次の例外、API 呼び出しで HTTP 500 を確認できています。例外の発生時点まで全構成で保証するものではありません。
>
> ```text
> System.InvalidOperationException: Services for database providers
> 'Microsoft.EntityFrameworkCore.SqlServer', 'Microsoft.EntityFrameworkCore.Sqlite'
> have been registered in the service provider. Only a single database provider can be
> registered in a service provider.
> ```
>
> 上のコードのように `IDbContextOptionsConfiguration<BloggingContext>` も併せて取り除いてください。

> [!TIP]
> ASP.NET Core 10 の標準 Web プロジェクトでは、トップレベルの `Program` はソースジェネレーターによって公開されるため、常に手動の宣言追加が必要というわけではありません。明示的に内部型とした構成などでは、Web プロジェクトの宣言を `public partial class Program { }` へ変更するか、**Web アセンブリ側からテストアセンブリに** `InternalsVisibleTo` を付与します。属性の向きを逆にしないでください（[Microsoft の生成器ソース](https://github.com/dotnet/aspnetcore/blob/v10.0.11/src/Framework/AspNetCoreAnalyzers/src/SourceGenerators/PublicTopLevelProgramGenerator.cs)）。

### リポジトリパターンとモック

データベースをまったく使わずにビジネスロジックだけをテストしたい場合は、データアクセスをインターフェイスの背後に隠します。

```csharp
public interface IBlogRepository
{
    Task<IReadOnlyList<Blog>> GetPopularBlogsAsync(
        int minRating,
        CancellationToken cancellationToken);
    Task AddAsync(Blog blog, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public class BlogRepository(BloggingContext context) : IBlogRepository
{
    public async Task<IReadOnlyList<Blog>> GetPopularBlogsAsync(
        int minRating, CancellationToken cancellationToken)
        => await context.Blogs
            .AsNoTracking()
            .Where(b => b.Rating >= minRating)
            .OrderByDescending(b => b.Rating)
            .ToListAsync(cancellationToken);

    public Task AddAsync(Blog blog, CancellationToken cancellationToken)
    {
        context.Blogs.Add(blog);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
        => await context.SaveChangesAsync(cancellationToken);
}
```

テストではインターフェイスの実装を差し替えます。

```csharp
public class FakeBlogRepository : IBlogRepository
{
    private readonly List<Blog> _blogs = [];

    public Task<IReadOnlyList<Blog>> GetPopularBlogsAsync(
        int minRating,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Blog>>(
            _blogs.Where(b => b.Rating >= minRating).OrderByDescending(b => b.Rating).ToList());

    public Task AddAsync(Blog blog, CancellationToken cancellationToken)
    {
        _blogs.Add(blog);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

> [!IMPORTANT]
> `DbSet<T>` や `IQueryable<T>` を直接モックすることは避けてください。LINQ to Objects は実際のプロバイダーのクエリ翻訳を実行しないため、比較・グループ化・NULL の扱いなどが異なりうるので、モックでは成功したクエリが実データベースで失敗することがあります。リポジトリのメソッドは `IQueryable` ではなく `IEnumerable` や `IAsyncEnumerable`、あるいは具体的なコレクションを返すようにします。

> [!NOTE]
> リポジトリを挟むと単体テストは容易になりますが、リポジトリには実装・保守する抽象化の層がもう 1 つ増えます。プロジェクトの規模とテスト方針を踏まえ、「データベースに対する統合テストで十分か」「ロジックの単体テストを高速に回したいか」を判断してください。

> [!WARNING]
> [第6章](../06-dependency-injection/index.md) は DI の例として各メソッドで `SaveChangesAsync` を呼ぶ形を示しています。[公式のトランザクションの説明](https://learn.microsoft.com/ja-jp/ef/core/saving/transactions)では、既定の原子性の範囲は 1 回の `SaveChanges` です。複数のリポジトリの変更をまとめて保存したい場合は、上の例のように保存を呼び出し側へ分けるなど、作業単位と保存・トランザクションの境界を合わせてください。外側の共有トランザクションの有無を問わず、各メソッドが必ず別トランザクションになるという意味ではありません。

## 2. 参考ドキュメント

- [EF Core アプリケーションのテスト | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/testing/)
- [運用データベースシステムに対するテスト | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/testing/testing-with-the-database)
- [運用データベースシステムを使用しないテスト | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/testing/testing-without-the-database)
- [SQLite プロバイダーの制限 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sqlite/limitations)
- [SQLite プロバイダーの値生成 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sqlite/value-generation)
- [SQLite プロバイダーの関数マッピング | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sqlite/functions)
- [EF Core 10.0 の新機能 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-10.0/whatsnew)
- [EF Core 10.0 の破壊的変更 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-10.0/breaking-changes)
- [EF Core 9.0 の破壊的変更 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes)
- [EF Core 8.0 の破壊的変更 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-8.0/breaking-changes)
- [SQLite のエラー処理 | Microsoft Learn](https://learn.microsoft.com/ja-jp/dotnet/standard/data/sqlite/database-errors)
- [SQLite の非同期処理と WAL | Microsoft Learn](https://learn.microsoft.com/ja-jp/dotnet/standard/data/sqlite/async)
- [同時実行の競合の処理 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/saving/concurrency)
- [DbContext の構成とスレッドの問題の回避 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/dbcontext-configuration/#avoiding-dbcontext-threading-issues)
- [ASP.NET Core の統合テスト | Microsoft Learn](https://learn.microsoft.com/ja-jp/aspnet/core/test/integration-tests?view=aspnetcore-10.0)
