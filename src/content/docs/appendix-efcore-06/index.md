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
   - [SQLite インメモリを使ったテスト](#sqlite-インメモリを使ったテスト)
   - [SQLite プロバイダーの制限を把握する](#sqlite-プロバイダーの制限を把握する)
   - [WebApplicationFactory で API ごとテストする](#webapplicationfactory-で-api-ごとテストする)
   - [リポジトリパターンとモック](#リポジトリパターンとモック)
2. [参考ドキュメント](#2-参考ドキュメント)

---

> [!NOTE]
> **この付録に載せた実測値の測定条件について。** 以下に出てくる数値は、次の環境で測定したものです。数値そのものはハードウェア・データ量・ネットワークによって大きく変わるため、**傾向（どちらが速いか、桁がいくつ違うか）を読み取る材料**として扱い、自分のアプリケーションでは必ず自分で計測してください。
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

xUnit では、テストクラス間でデータベースのセットアップを共有するために **フィクスチャ** を使います。

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

書き込みを伴うテストは、トランザクションを開始してコミットしないことで、テスト間の独立性を保てます。

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
> CI 環境で本番と同じデータベースを用意するには、**Testcontainers** のようなライブラリで Docker コンテナーを起動する方法が便利です。テストの開始時にコンテナーを起動し、終了時に破棄することで、環境に依存しない再現性の高いテストを構築できます。

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
> - **`DateTimeOffset` を大小比較や `ORDER BY` に使えません。** SQLite プロバイダーは `DateTimeOffset` の値を格納でき、**等価比較（`==`）は翻訳されます**が、大小比較と並べ替えは翻訳できず実行時に例外になります（実測で確認）。しかも例外の型とメッセージが操作によって異なります。
>
>   `Where(e => e.At > pivot)` のような大小比較では、翻訳に失敗した旨の `InvalidOperationException` になります。
>
>   ```text
>   System.InvalidOperationException: The LINQ expression 'DbSet<Ev>()
>       .Where(e => e.At > @pivot)' could not be translated. Either rewrite the query in a form
>   that can be translated, or switch to client evaluation explicitly by inserting a call to
>   'AsEnumerable', 'AsAsyncEnumerable', 'ToList', or 'ToListAsync'.
>   ```
>
>   `OrderBy(e => e.At)` では、SQLite プロバイダー固有の `NotSupportedException` になります。
>
>   ```text
>   System.NotSupportedException: SQLite does not support expressions of type 'DateTimeOffset'
>   in ORDER BY clauses. Convert the values to a supported type, or use LINQ to Objects to order
>   the results on the client side.
>   ```
>
>   メッセージは「クライアント側で評価せよ」と促しますが、`Where` や `OrderBy` の中身をクライアント評価に切り替えることは EF Core では既定で禁止されており、上のように例外になります。**エンティティで `DateTimeOffset` を使っている場合、その列に対する絞り込みや並べ替えは SQLite ではテストできません。**
> - `rowversion` による同時実行トークンは SQL Server 固有の機能で、SQLite では自動的に更新されません。
> - `decimal` の精度、`ALTER TABLE` の対応範囲、スキーマ（名前空間）の扱いが異なります。
> - `dotnet ef migrations script --idempotent` は SQLite ではサポートされません。実行すると `Generating idempotent scripts for migrations is not currently supported for SQLite.` というエラーで失敗します（実測で確認）。
> - 主キーの値の生成方法が異なります。SQL Server では `IDENTITY` 列になりますが、SQLite では規約により `AUTOINCREMENT` が付きます。
>
> SQL Server 固有の機能を使っている箇所は、実データベースに対する統合テストで確認してください。

> [!TIP]
> **SQLite の `AUTOINCREMENT` は EF Core 10 から設定で切り替えられるようになりました。** SQLite プロバイダーは規約により、複合キーの一部でもなく外部キーも持たない整数の主キーに `AUTOINCREMENT` を付けます。公式ドキュメントは、`AUTOINCREMENT` が SQLite の既定のキー生成方式である [ROWID](https://sqlite.org/lang_createtable.html#rowid) に比べて **CPU・メモリ・ディスク容量・ディスク I/O のオーバーヘッドを追加する** と説明しています。その代わり `ROWID` は削除された行の値を再利用するため、値の再利用が問題にならない場合にだけ無効化を検討してください。
>
> 実際に生成される DDL は次のとおりです（実測で確認）。
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
>         .Metadata.SetValueGenerationStrategy(SqliteValueGenerationStrategy.None);
> }
> ```
>
> 逆に、値変換を挟んでいるなどの理由で規約が働かない場合は、`UseAutoincrement()` で明示的に有効化できます。なお `ValueGeneratedNever()` を使う場合は、保存前にアプリケーション側が値を用意する必要があります。この指定はデータベース側の値生成までは止めないため、EF Core を経由しない書き込みでは依然として値が生成される点にも注意してください。

> [!WARNING]
> **EF Core 10（Microsoft.Data.Sqlite 10.0）では、SQLite のタイムゾーンの扱いに重大度「高」の破壊的変更が入りました。** オフセットを持たないテキストのタイムスタンプ（例: `2026-08-31 12:00:00`）を `DateTimeOffset` として読み出したとき、以前は **ローカルタイムゾーン** の値とみなしていましたが、EF Core 10 からは **UTC** とみなすようになりました。
>
> 実際に日本時間（UTC+9）の環境で、オフセットなしの値が入った列を読み出して比較すると、次のように解釈が 9 時間ずれます（実測で確認）。
>
> ```text
> EF Core 10 の既定 : 2026-08-31T12:00:00+00:00 （UTC 12:00 とみなす）
> EF Core 9 までの挙動: 2026-08-31T12:00:00+09:00 （UTC 03:00 とみなす）
> ```
>
> EF Core が書き込んだ値にはオフセットが付くため往復は一致しますが、**他のシステムや旧バージョンが書き込んだオフセットなしのデータを読む場合は結果が変わります。** すぐに修正できない場合の一時的な回避策として、次の `AppContext` スイッチで従来の挙動に戻せます（公式は「最後の手段」と位置づけています）。
>
> ```csharp
> AppContext.SetSwitch("Microsoft.Data.Sqlite.Pre10TimeZoneHandling", isEnabled: true);
> ```
>
> **なお、重大度「高」の破壊的変更はこれを含めて 3 つあります。** 残る 2 つも日本時間（UTC+9）の環境で実測しました。
>
> | 変更点 | 実測した挙動 |
> | --- | --- |
> | `DateTimeOffset` を `REAL` 列へ書き込むと UTC で保存される | `2026-08-31 12:00+09:00` を書くとユリウス日 `2461283.625`（= UTC 03:00）が格納され、読み戻すと `2026-08-31T03:00:00+00:00` になる。**元のオフセット `+09:00` は失われる** |
> | オフセット付きの値を `GetDateTime` で読むと UTC で返る | `2026-08-31 12:00:00+09:00` を `GetDateTime` で読むと `2026-08-31T03:00:00Z`（`Kind` は `Utc`）が返る |
>
> どちらも「オフセットを保持したい」用途では意図しない結果になります。SQLite に日時を保存するときは、`DateTimeOffset` をそのまま渡すのではなく **UTC の `DateTime` に統一して保存し、表示時にタイムゾーンを適用する** 設計にしておくと、これらの差異の影響を受けません。

### SQLite プロバイダーの制限を把握する

前節のとおり SQLite はテストの実行先として有力ですが、**本番が SQL Server なら SQLite で通ったテストが本番で通る保証はありません。** 公式が挙げている制限のうち、実際に踏みやすいものを SQLite 上で確認しました。

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

危険なのは表の下 3 行です。**例外が出ないため、テストは通ってしまいます。**

- スキーマ指定は静かに捨てられるので、SQL Server で `sales.Items` と `dbo.Items` を使い分けている設計は SQLite 上では区別されません。
- `IsRowVersion()` は列こそ作られますが、SQLite はデータベース側で値を生成しないため、**同時実行制御が働きません。** 公式は「データベースが生成する同時実行トークンは未サポート」と明記しています。楽観的同時実行の失敗を再現するテストは、SQLite では書けないと考えてください。

> [!NOTE]
> `decimal` の扱いは EF Core 10 で改善されました。以前は大小比較と並べ替えがクライアント評価を必要としましたが、EF Core 10 は `ef_compare()` という独自関数と `EF_DECIMAL` という独自の照合順序を接続に登録し、データベース側で処理します。ただし `TEXT` 格納であることは変わらないため、SQL Server の `decimal(18, 2)` と厳密に同じ丸めになるとは限りません。

#### Unhex は null を返すことがある

SQLite プロバイダーには `EF.Functions.Unhex()` があり、16 進数表記の文字列をバイト配列に変換します。SQLite の `unhex` 関数に翻訳されますが、**入力が正しい 16 進数でなければ `NULL` が返ります**。

```csharp
var q = db.Hexes.Select(x => EF.Functions.Unhex(x.S));
// SELECT unhex("h"."S") FROM "Hexes" AS "h"
```

`48656C6C6F`（`Hello`）と、16 進数ではない文字列の 2 行で実行すると、後者は `null` になりました（実測）。

```text
結果: Hello
結果: null
```

EF Core 9 以降、`Unhex()` の戻り値は `byte[]?` として宣言されています（実測でも Null 許容と確認）。以前は `byte[]` と宣言されていて、実際には `null` が返るのに注釈が食い違っていました。

```csharp
// 正しい 16 進数だと確信できる場合
var data = await db.Hexes.Select(b => EF.Functions.Unhex(b.S)!).ToListAsync();

// そうでない場合は null チェックを入れる
```

#### 先行書き込みログ (WAL) が有効かを確認する

EF Core 7 以降、SQLite プロバイダーは `RETURNING` 句を使って保存します。この方式は効率的ですが、**テーブルがロックされているときに自動で再試行しません。** 先行書き込みログ (write-ahead logging: WAL) が無効なデータベースを Web アプリケーションのような多スレッド環境で使うと、ロック関連のエラーに遭遇しやすくなります。

`journal_mode` を実測すると、経路によって既定値が違いました。

| 作成方法 | `PRAGMA journal_mode` |
| --- | --- |
| EF Core が作成したデータベース | `wal` |
| `SqliteConnection` で直接作成したデータベース | `delete` |

公式も「EF によって作成されたデータベースでは、既定で先行書き込みログが有効になる」と説明しています。危険なのは**既存のファイルを引き継ぐ場合**です。EF Core 以外の手段で作られたデータベースファイルには WAL が設定されていない可能性があるため、確認して必要なら有効にしてください。SQLite では次の `PRAGMA` で切り替えます。

```sql
PRAGMA journal_mode = 'wal';
```

#### マイグレーションの制限

SQLite は `ALTER TABLE` でできることが極端に少ないため、EF Core は多くの変更を**テーブルの作り直し**で実現します。`Note` 列を削除して `Rank` 列を追加するマイグレーションで、実際に生成された SQL は次のとおりでした。

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

列を 1 つ落とすだけで、全行のコピーと外部キー検査の一時停止が発生します。行数の多いテーブルでは所要時間と一時的なディスク使用量に注意してください。

また、[SQL スクリプトとマイグレーションバンドル](../08-entity-framework-core/index.md#sql-スクリプトとマイグレーションバンドル)で紹介した冪等スクリプトは、SQLite では生成できません。

```text
Generating idempotent scripts for migrations is not currently supported for SQLite.
```

> [!WARNING]
> **同じ `DbContext` インスタンスの並行利用も、SQLite では表面化しないことがあります。** SQLite のように同期的な I/O を行うプロバイダーでは、`Task.WhenAll(context.Blogs.ToListAsync(), context.Posts.ToListAsync())` のような書き方をしても各クエリが順番に完了してしまい、[同時実行検出](/appendix-efcore-05/#同時実行検出を無効にしてはいけない)の例外が発生しないことがあります（実際に 20 回試行して 1 度も発生しませんでした）。一方、`Task.Run` で明確に別スレッドから実行すると確実に例外になります。
>
> つまり **SQLite を使った単体テストでは問題が表面化せず、本番の SQL Server で初めて落ちる**、ということが起こり得ます。「テストが通ったから安全」と考えず、1 つの `DbContext` インスタンスを複数の処理で共有しない設計を徹底してください。

> [!TIP]
> 制限を回避しつつテストを速く保つ現実的な折衷案は、**単体テストは SQLite、スキーマや同時実行が絡むテストは本番と同じデータベースエンジン**、と使い分けることです。SQL Server なら [Testcontainers](https://dotnet.testcontainers.org/) や開発者向けのコンテナーイメージで、テスト実行時に本物を立てられます。

### WebApplicationFactory で API ごとテストする

ASP.NET Core の統合テストでは、`WebApplicationFactory<TEntryPoint>` でアプリケーション全体を起動し、テスト用のデータベースに差し替えます。

```csharp
using System.Data.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public class BloggingApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // 既定の DbContext 構成を取り除く
            services.RemoveAll<DbContextOptions<BloggingContext>>();
            services.RemoveAll<DbContextOptions>();

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

> [!TIP]
> `Program.cs` がトップレベルステートメントで書かれている場合、テストプロジェクトから `Program` クラスを参照するために、Web プロジェクト側に `public partial class Program { }` を追加するか、テストプロジェクトから `InternalsVisibleTo` を設定する必要があります。

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
> `DbSet<T>` や `IQueryable<T>` を直接モックすることは避けてください。LINQ to Objects と LINQ to Entities では、`string.Compare` の挙動、`GroupBy` の変換可否、NULL の比較などの挙動が異なり、モックでは成功したクエリが実データベースで失敗することがあります。リポジトリのメソッドは `IQueryable` ではなく `IEnumerable` や `IAsyncEnumerable`、あるいは具体的なコレクションを返すようにします。

> [!NOTE]
> リポジトリを挟むと単体テストは容易になりますが、EF Core の `DbSet` はすでにリポジトリパターンの実装であり、層をもう 1 つ追加することになります。プロジェクトの規模とテスト方針を踏まえ、「データベースに対する統合テストで十分か」「ロジックの単体テストを高速に回したいか」を判断してください。

> [!WARNING]
> [第6章](../06-dependency-injection/index.md) では DI の説明としてリポジトリを取り上げ、各メソッドの中で `SaveChangesAsync` を呼ぶ形を示しました。実際のデータアクセス層では、上の例のように **`SaveChangesAsync` をリポジトリの外（あるいは専用のメソッド）に切り出すことを推奨します。** 1 回の処理で複数のリポジトリを更新したいとき、メソッドごとに保存してしまうと、それぞれが別のトランザクションになり、途中で失敗したときに一部だけ反映された状態が残るからです。トランザクションの境界は、リポジトリではなく呼び出し側（アプリケーション層）が決めるべきものです。

## 2. 参考ドキュメント

- [EF Core アプリケーションのテスト | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/testing/)
- [運用データベースシステムに対するテスト | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/testing/testing-with-the-database)
- [運用データベースシステムを使用しないテスト | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/testing/testing-without-the-database)
- [SQLite プロバイダーの制限 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sqlite/limitations)
- [SQLite プロバイダーの値生成 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sqlite/value-generation)
- [ASP.NET Core の統合テスト | Microsoft Learn](https://learn.microsoft.com/ja-jp/aspnet/core/test/integration-tests?view=aspnetcore-10.0)
