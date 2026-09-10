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

> [!WARNING]
> **接続文字列の `(localdb)\mssqllocaldb` は Windows でしか動きません。** LocalDB はインストール後に個別サーバーの管理作業を抑えてテスト用の SQL Server を起動できて便利ですが、公式ドキュメントは次の問題点を挙げています。
>
> - SQL Server Developer Edition がサポートする機能のすべてには対応していない
> - **Windows でしか利用できない**
> - サービスが起動するため、初回のテスト実行が遅れる
>
> macOS で LocalDB の接続文字列を使い、最小 Context から `EnsureCreated()` を呼んだところ、次の例外になりました。上のフィクスチャ全体をそのまま実行した結果ではありません。
>
> ```text
> System.PlatformNotSupportedException: LocalDB はこのプラットフォームでサポートされていません。
> ```
>
> 公式は「**LocalDB ではなく SQL Server Developer Edition をインストールすることを一般に推奨する**。完全な SQL Server の機能セットが手に入り、導入も概して容易だからだ」と述べています。macOS や Linux では、後述の Testcontainers か Docker のコンテナーイメージを使い、接続文字列を `Server=localhost,1433;User Id=sa;...` の形にしてください。

> [!NOTE]
> フィクスチャのコンストラクターでロックと静的フラグを使っているのには理由があります。フィクスチャを 1 つのテストクラスでしか使わないなら、xUnit が 1 回だけ生成することを保証します。しかし同じフィクスチャを複数のテストクラスで共有するのはよくあることです。xUnit の**コレクションフィクスチャ**を使えば共有できますが、公式ドキュメントが指摘するとおり、**同じコレクションに属するテストクラスは並列実行されないため、性能に影響する可能性があります**。そこでクラスフィクスチャのまま、データベースの作成とシードをロックで囲み、静的フラグで二度実行されないようにしています。

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

前節の「トランザクションを開始してコミットしない」方式には、**テスト対象のコード自身がトランザクションを明示的に管理している場合には使えない**という制約があります。公式ドキュメントは「データベースは通常、入れ子のトランザクションをサポートしないため、上記のようにトランザクションを分離に使うことはできない。製品コードがそれを使う必要があるからだ」と説明しています。

実際に、テスト側でトランザクションを開始した状態で製品コードが `BeginTransactionAsync(IsolationLevel.Serializable)` を呼ぶ状況を SQL Server 2022 で再現すると、次の例外になりました（実測で確認）。

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

#### クリーンアップは生の SQL のほうが速い

コミットを伴うテストでは、テスト間で変更が残らないよう、削除などの後片付けが必要です。EF Core の API で消すこともできますが、公式は「これは通常、テーブルを消す最も効率的な方法ではない。テストの速度が気になるなら、生の SQL でテーブルを削除するとよい」と述べています。

1,000 行を消す時間を SQL Server 2022 で比較しました（実測）。

| 方法 | 所要時間 |
| --- | --- |
| `RemoveRange` + `SaveChangesAsync` | 443 ms |
| `ExecuteSqlRawAsync("DELETE FROM [Blogs]")` | 9 ms |

この条件では 443 / 9 ≒ 49.2 倍でした。比較した EF 側は全行を読み込んで `RemoveRange` と `SaveChanges` で削除する方式です。`ExecuteDelete` などを含む EF Core の削除 API 全般が常に 50 倍遅いという結果ではありません。

```csharp
public async Task CleanupAsync()
{
    await using var context = CreateContext();
    await context.Database.ExecuteSqlRawAsync("DELETE FROM [Blogs]");
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
> **既定値は有効 (`true`) で、公式は「ごく大多数のケースでは既定の動作が推奨される」と明記しています。** 無効にすると `DbContext` の作成が大幅に遅くなるためです。SQLite で `DbContext` を 100 回生成して単純なクエリを実行する時間を比較しました（実測）。
>
> | 設定 | 所要時間 |
> | --- | --- |
> | `EnableServiceProviderCaching(true)`（既定） | 52 ms |
> | `EnableServiceProviderCaching(false)` | 439 ms |
>
> 8 倍以上の差になりました。公式は「内部サービスが正しくないという問題があるなら、別の方法で直すべきだ」とも述べています。問題のある構成を分離するなど、理由が明確なテスト用途で無効化を検討してください。

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
> - **`DateTimeOffset` を大小比較や `ORDER BY` に使えません。** SQLite プロバイダーは `DateTimeOffset` の値を格納でき、**等価比較（`==`）は翻訳されます**が、大小比較と並べ替えは翻訳できず実行時に例外になります（実測で確認）。しかも例外の型とメッセージが操作によって異なります。
>
>   `Where(e => e.At > pivot)` のような大小比較では、翻訳に失敗した旨の `InvalidOperationException` になります（以下は読みやすく改行し、末尾の案内を省略した抜粋です）。
>
>   ```text
>   System.InvalidOperationException: The LINQ expression 'DbSet<Ev>()
>       .Where(e => e.At > @pivot)' could not be translated. Either rewrite the query in a form
>   that can be translated, or switch to client evaluation explicitly by inserting a call to
>   'AsEnumerable', 'AsAsyncEnumerable', 'ToList', or 'ToListAsync'.
>   ```
>
>   `OrderBy(e => e.At)` では、SQLite プロバイダー固有の `NotSupportedException` になります（以下は抜粋です）。
>
>   ```text
>   System.NotSupportedException: SQLite does not support expressions of type 'DateTimeOffset'
>   in ORDER BY clauses. Convert the values to a supported type, or use LINQ to Objects to order
>   the results on the client side.
>   ```
>
>   メッセージは「クライアント側で評価せよ」と促しますが、`Where` や `OrderBy` の中身をクライアント評価に切り替えることは EF Core では既定で禁止されており、上のように例外になります。**ここで失敗するのは `DateTimeOffset` の大小比較や並べ替えです。等価比較まで一律に禁止されるわけではありません。** 必要ならデータを取得後に明示的にクライアント評価できますが、実データベース側の比較動作を検証する代替にはなりません。
> - `rowversion` による同時実行トークンは SQL Server 固有の機能で、SQLite では自動的に更新されません。
> - `decimal` の精度、`ALTER TABLE` の対応範囲、スキーマ（名前空間）の扱いが異なります。
> - `dotnet ef migrations script --idempotent` は SQLite ではサポートされません。実行すると `Generating idempotent scripts for migrations is not currently supported for SQLite.` というエラーで失敗します（実測で確認）。
> - 規約の対象となる整数主キーでは、値の生成方法が異なります。SQL Server では `IDENTITY` 列になりますが、SQLite では規約により `AUTOINCREMENT` が付きます。
>
> SQL Server 固有の機能を使っている箇所は、実データベースに対する統合テストで確認してください。

> [!TIP]
> **SQLite の `AUTOINCREMENT` は EF Core 10 から設定で切り替えられるようになりました。** SQLite プロバイダーは規約により、値生成が構成され、値変換がなく、複合キーの一部でも外部キーでもない整数主キーに `AUTOINCREMENT` を付けます。公式ドキュメントは、`AUTOINCREMENT` が SQLite の既定のキー生成方式である [ROWID](https://sqlite.org/lang_createtable.html#rowid) に比べて **CPU・メモリ・ディスク容量・ディスク I/O のオーバーヘッドを追加する** と説明しています。その代わり `ROWID` は削除された行の値を再利用するため、値の再利用が問題にならない場合にだけ無効化を検討してください。
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
> 実際に日本時間（UTC+9）の環境で、Microsoft.Data.Sqlite 10.0.11 で既定と互換スイッチ有効の別プロセスからオフセットなしの値を読み出すと、次のように解釈が 9 時間ずれます（実測で確認）。
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
> このスイッチは SQLite の読み取り処理の型が初期化されるときに一度だけ読み取られます。[EF Core 10.0.11 の公式実装](https://github.com/dotnet/efcore/blob/v10.0.11/src/Microsoft.Data.Sqlite.Core/SqliteValueReader.cs#L14-L15)と実測でも、初回読み取り後に有効へ変えても結果は UTC のままでした。別プロセスで最初の SQLite 操作前に設定した場合は、日本時間のローカルオフセット `+09:00` で読み取れました。
>
> **なお、重大度「高」の破壊的変更はこれを含めて 3 つあります。** 残る 2 つも Microsoft.Data.Sqlite 10.0.11 を直接使い、日本時間（UTC+9）の環境で実測しました。
>
> | 変更点 | 実測した挙動 |
> | --- | --- |
> | パラメーターに `SqliteType.Real` を指定して `DateTimeOffset` を書き込むと UTC で保存される | `2026-08-31 12:00+09:00` を書くとユリウス日 `2461283.625`（= UTC 03:00）が格納され、読み戻すと `2026-08-31T03:00:00+00:00` になる。**元のオフセット `+09:00` は失われる** |
> | オフセット付きの値を `GetDateTime` で読むと UTC で返る | `2026-08-31 12:00:00+09:00` を `GetDateTime` で読むと `2026-08-31T03:00:00Z`（`Kind` は `Utc`）が返る |
>
> オフセット自体を保持したい用途では、保存形式と読み取り API を確認してください。UTC の `DateTime` に統一して表示時にタイムゾーンを適用する設計は公式の推奨ですが、それだけですべての既存データや API の差異がなくなるわけではありません。特にオフセットなしの既存値は、その値が表す時刻を決めてから移行します。

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

特に、スキーマ指定と `IsRowVersion()` は、**テーブルを作成できたことだけでは本番と同じ動作を確認できません。** `HasSequence<int>()` は非対応の例外になるため、区別してください。

- スキーマ指定は警告が出たうえで無視されるので、SQL Server で `sales.Items` と `dbo.Items` を使い分けている設計は SQLite 上では区別されません。
- `IsRowVersion()` で列は作れますが、SQLite は SQL Server の `rowversion` のようにトークンを自動生成・更新しません。**SQL Server の自動生成まで含めた同時実行制御の代用にはなりません。** `BLOB NOT NULL` の列を作り、既定値やトリガーを用意せずに挿入すると、実測では `NOT NULL constraint failed` を内部例外とする `DbUpdateException` になりました。

一方、**アプリケーション側でトークンを管理する楽観的同時実行制御は SQLite でもテストできます。** 公式ドキュメントも、SQLite のように自動更新型を持たないデータベースでこの方式を使えると明記しています。`Guid` のプロパティに `IsConcurrencyToken()` を設定し、保存時にアプリケーションで新しい値を割り当てた実測では、次の結果になりました（EF Core 10.0.11、SQLite のインメモリデータベース）。

| 先に保存する側の処理 | 古いトークンを持つ側の処理 | 結果 |
| --- | --- | --- |
| データとトークンを更新 | 更新して保存 | `DbUpdateConcurrencyException` |
| データとトークンを更新 | 削除して保存 | `DbUpdateConcurrencyException` |
| データだけ更新し、トークンは変更しない | 更新して保存 | 競合を検出せず、後の値で上書き |

2 つの `DbContext` で同じ行を読んでから順に保存し、先行更新後の古いトークンでの保存を確認しています。**トークンの再生成はアプリケーションの責任です。** 本番が `rowversion` を使うなら、その自動更新の検証は SQL Server で行ってください。トークンの設定と競合解決は[付録4の「楽観的同時実行制御」](../appendix-efcore-04/index.md#楽観的同時実行制御)で扱います。

> [!NOTE]
> `decimal` の扱いは EF Core 10 で改善されました。以前は大小比較と並べ替えがクライアント評価を必要としましたが、EF Core 10 は `ef_compare()` という独自関数と `EF_DECIMAL` という独自の照合順序を接続に登録し、データベース側で処理します。ただし `TEXT` 格納であることは変わらないため、SQL Server の `decimal(18, 2)` と厳密に同じ丸めになるとは限りません。

#### Math のメソッドはサーバー側で評価される

EF Core 8 以降、`Math` クラスのメソッドは、対応する SQLite の数学関数があるものはすべて SQL に翻訳されます。EF Core 7 までは `Abs` / `Max` / `Min` / `Round` だけが翻訳され、それ以外は最終的な `Select` 式に現れた場合にクライアント側で評価されていました。

```csharp
var q = db.Ms.Select(x => new
{
    P = Math.Pow(x.V, 2),
    Sq = Math.Sqrt(x.V),
    L = Math.Log(x.V),
    E = Math.Exp(x.V),
});
```

実際に発行された SQL は次のとおりで、4 つとも SQLite の関数に翻訳されていました（実測）。`Math.Log` が `ln` になる点に注意してください。

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

EF Core 7 以降の SQLite プロバイダーは保存に `RETURNING` を使います。公式の破壊的変更には WAL 無効時のロック競合への注意がありますが、Microsoft.Data.Sqlite の busy / locked に対するタイムアウトまでの再試行まで一律になくなるという意味ではありません。EF Core 10.0.11 / SQLite の追試では、WAL 無効・別接続の書き込みロックを 300 ミリ秒後に解除すると、`RETURNING` 付きの保存は待機後に成功しました。この条件で EF の実行戦略は再試行無効でした。すべてのロック競合が回復する保証ではないため、WAL とタイムアウト、実際の並行処理を確認してください。詳しくは[SQLite のエラー処理](https://learn.microsoft.com/ja-jp/dotnet/standard/data/sqlite/database-errors)と [WAL](https://learn.microsoft.com/ja-jp/dotnet/standard/data/sqlite/async) の説明を参照してください。

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

この変更に対して生成されたスクリプトには、全行のコピーと外部キー検査の一時停止が含まれます。すべての列削除で必ず同じスクリプトになるという意味ではありません。行数の多いテーブルでは所要時間と一時的なディスク使用量に注意してください。

また、[SQL スクリプトとマイグレーションバンドル](../08-entity-framework-core/index.md#sql-スクリプトとマイグレーションバンドル)で紹介した冪等スクリプトは、SQLite では生成できません。

```text
Generating idempotent scripts for migrations is not currently supported for SQLite.
```

> [!WARNING]
> **同じ `DbContext` インスタンスの並行利用も、SQLite では表面化しないことがあります。** SQLite のように同期的な I/O を行うプロバイダーでは、`Task.WhenAll(context.Blogs.ToListAsync(), context.Posts.ToListAsync())` のような書き方をしても各クエリが順番に完了してしまい、[同時実行検出](../appendix-efcore-05/index.md#同時実行検出を無効にしてはいけない)の例外が発生しないことがあります（別の最小モデルで、4 本の取得タスクを順に作成してから `Task.WhenAll` を待つ処理を 20 回行い、例外なしを観測しました。左の Blogs / Posts 2 本そのものを測った結果ではなく、複数スレッドで同時開始を保証した試験でもありません）。
>
> **`Task.Run` を使えば必ず例外になるわけでもありません。** 2 回の `Task.Run` をそれぞれ直ちに `await` した場合は成功し、先のクエリを実行中に待機させて別のクエリを重ねた場合は `InvalidOperationException` になりました（EF Core 10.0.11 で実測）。公式が禁止しているのは同じコンテキストでの**操作の重複**であり、未検出の場合も未定義の動作やデータ破損の可能性があると明記しています。例外の有無を安全性の判定に使わないでください。
>
> つまり **SQLite を使った単体テストでは問題が表面化せず、本番の SQL Server で初めて落ちる**、ということが起こり得ます。「テストが通ったから安全」と考えず、1 つの `DbContext` インスタンスを複数の処理で並行利用しない設計を徹底してください。

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
> **`DbContextOptions` を `RemoveAll` しただけでは、既定のプロバイダーは外れません。** `AddDbContext` は `DbContextOptions<TContext>` と `DbContextOptions` のほかに `IDbContextOptionsConfiguration<TContext>` を登録します。オプションの組み立ては後者を通じて行われるため、前の 2 つだけを取り除いて `UseSqlite` を追加すると、**本番用のプロバイダーとテスト用のプロバイダーが両方登録された状態**になります。
>
> この状態では、`DbContext` インスタンスの DI 解決自体は成功しても、モデルを初期化する段階で次の例外が発生します。`context.Model` へのアクセスで確認でき、API のクエリ実行でも発生します（実測では API 呼び出しが 500 になりました）。
>
> ```text
> System.InvalidOperationException: Services for database providers
> 'Microsoft.EntityFrameworkCore.SqlServer', 'Microsoft.EntityFrameworkCore.Sqlite'
> have been registered in the service provider. Only a single database provider can be
> registered in a service provider.
> ```
>
> 上のコードのように `IDbContextOptionsConfiguration<BloggingContext>` も併せて取り除いてください。公式ドキュメントも「別のプロバイダーを構成しても以前のプロバイダー構成は削除されない。完全に置き換えるにはコンテキストの登録を取り除いて追加し直すか、新しいサービスコレクションを作る必要がある」と説明しています。

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
> [第6章](../06-dependency-injection/index.md) では DI の説明としてリポジトリを取り上げ、各メソッドの中で `SaveChangesAsync` を呼ぶ形を示しました。実際のデータアクセス層では、上の例のように **`SaveChangesAsync` をリポジトリの外（あるいは専用のメソッド）に切り出すことを推奨します。** 1 回の処理で複数のリポジトリを更新したいとき、メソッドごとに保存してしまうと、それぞれが別のトランザクションになり、途中で失敗したときに一部だけ反映された状態が残るからです。トランザクションの境界は、リポジトリではなく呼び出し側（アプリケーション層）が決めるべきものです。

## 2. 参考ドキュメント

- [EF Core アプリケーションのテスト | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/testing/)
- [運用データベースシステムに対するテスト | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/testing/testing-with-the-database)
- [運用データベースシステムを使用しないテスト | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/testing/testing-without-the-database)
- [SQLite プロバイダーの制限 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sqlite/limitations)
- [SQLite プロバイダーの値生成 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sqlite/value-generation)
- [同時実行の競合の処理 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/saving/concurrency)
- [DbContext の構成とスレッドの問題の回避 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/dbcontext-configuration/#avoiding-dbcontext-threading-issues)
- [ASP.NET Core の統合テスト | Microsoft Learn](https://learn.microsoft.com/ja-jp/aspnet/core/test/integration-tests?view=aspnetcore-10.0)
