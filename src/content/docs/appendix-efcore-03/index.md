---
title: "付録 EF Core 3：マイグレーションの詳細とクエリ"
description: "EF Core のマイグレーションの読み方と運用、単一クエリと分割クエリ、照合順序、生の SQL、ユーザー定義関数とビューのマッピングを解説します。第8章の付録です。"
---

このページは [第8章：データベースアクセスと ORM (Entity Framework Core)](../08-entity-framework-core/index.md) の付録です。マイグレーションの詳細な扱い方と、クエリの制御、SQL を直接扱う方法を扱います。

本編を先に読んでから、必要な項目をここで参照してください。

**第8章のほかの付録**

- [付録 EF Core 1：モデル定義（エンティティとリレーションシップ）](../appendix-efcore-01/index.md)
- [付録 EF Core 2：モデル定義（キー・採番・SQL Server 固有）](../appendix-efcore-02/index.md)
- [付録 EF Core 4：更新・トランザクション・レプリカ](../appendix-efcore-04/index.md)
- [付録 EF Core 5：パフォーマンス](../appendix-efcore-05/index.md)
- [付録 EF Core 6：テスト](../appendix-efcore-06/index.md)


---

## 目次

1. [マイグレーションの詳細](#1-マイグレーションの詳細)
   - [生成されたマイグレーションを読む](#生成されたマイグレーションを読む)
   - [既定値の制約に名前を付ける](#既定値の制約に名前を付ける)
   - [EF Core ツールの使い分け](#ef-core-ツールの使い分け)
   - [SQL スクリプトとマイグレーションバンドルを使い分ける](#sql-スクリプトとマイグレーションバンドルを使い分ける)
   - [マイグレーションバンドルの対象ランタイムを指定する](#マイグレーションバンドルの対象ランタイムを指定する)
   - [モデルとマイグレーションのずれを検出する](#モデルとマイグレーションのずれを検出する)
   - [同時にマイグレーションが走らないようにする](#同時にマイグレーションが走らないようにする)
   - [初期データの投入（シード）](#初期データの投入シード)
   - [既存データベースからスキャフォールディングする](#既存データベースからスキャフォールディングする)
   - [設計時 DbContext ファクトリ](#設計時-dbcontext-ファクトリ)
2. [クエリの制御](#2-クエリの制御)
   - [単一クエリと分割クエリ](#単一クエリと分割クエリ)
   - [LeftJoin / RightJoin 演算子](#leftjoin--rightjoin-演算子)
   - [大文字小文字の区別は照合順序が決める](#大文字小文字の区別は照合順序が決める)
   - [複数の列で並べ替えるキーセットページング](#複数の列で並べ替えるキーセットページング)
   - [関連データを読み込まずに数える](#関連データを読み込まずに数える)
   - [常に Include する（AutoInclude）](#常に-include-するautoinclude)
   - [null の比較は C# と SQL で意味が違う](#null-の比較は-c-と-sql-で意味が違う)
   - [エンティティをそのまま JSON にすると循環参照で失敗する](#エンティティをそのまま-json-にすると循環参照で失敗する)
3. [SQL を直接扱う](#3-sql-を直接扱う)
   - [生の SQL を使う](#生の-sql-を使う)
   - [ユーザー定義関数とビューをマッピングする](#ユーザー定義関数とビューをマッピングする)
   - [SQL Server 固有の関数を LINQ から呼ぶ](#sql-server-固有の関数を-linq-から呼ぶ)
4. [参考ドキュメント](#4-参考ドキュメント)

---

## 1. マイグレーションの詳細

### 生成されたマイグレーションを読む

生成されるマイグレーションは通常の C# コードです。内容を確認し、必要なら手を入れられます。

```csharp
public partial class AddPostPublishedAt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "PublishedAt",
            table: "Posts",
            type: "datetimeoffset",
            nullable: false,
            defaultValue: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        migrationBuilder.CreateIndex(
            name: "IX_Posts_PublishedAt",
            table: "Posts",
            column: "PublishedAt");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Posts_PublishedAt", table: "Posts");
        migrationBuilder.DropColumn(name: "PublishedAt", table: "Posts");
    }
}
```

`Up` が適用時、`Down` がロールバック時の処理です。データの移行が必要な場合は `migrationBuilder.Sql("...")` で任意の SQL を挟めます。

```csharp
migrationBuilder.Sql("UPDATE [Posts] SET [PublishedAt] = [CreatedAt] WHERE [PublishedAt] IS NULL;");
```

> [!WARNING]
> プロパティ名を変更した場合、EF Core は「列の削除」と「列の追加」として差分を検出することがあります。そのまま適用するとデータが失われるため、生成されたマイグレーションを確認し、必要に応じて `migrationBuilder.RenameColumn(...)` に書き換えてください。公式ドキュメントも「どの適用方法を選ぶ場合でも、必ず生成されたマイグレーションを検査し、本番データベースに適用する前にテストすること」と明記しています。

### 既定値の制約に名前を付ける

列に既定値を設定すると、SQL Server 側には **既定値制約 (default constraint)** が作られます。名前を指定しない場合、制約名はデータベースが自動生成します。実際に名前を指定せずテーブルを作り、`sys.default_constraints` を引いたところ、次の名前が付いていました。

```text
DF__Posts__CreatedDa__49C3F6B7
DF__Posts__Views__4AB81AF0
```

末尾は毎回変わるため、名前が分からないと後から `ALTER TABLE ... DROP CONSTRAINT` で外すのが面倒になります。

EF Core 10 では、`HasDefaultValueSql` / `HasDefaultValue` の第 2 引数で制約名を指定できるようになりました。

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Post>()
        .Property(p => p.CreatedDate)
        .HasDefaultValueSql("GETDATE()", "DF_Post_CreatedDate");
}
```

生成されるマイグレーションには注釈として制約名が入り、SQL Server 向けの DDL では `CONSTRAINT` 句として出力されます。

```sql
CREATE TABLE [Posts] (
    [Id] int NOT NULL IDENTITY,
    [CreatedDate] datetime2 NOT NULL CONSTRAINT [DF_Post_CreatedDate] DEFAULT (GETDATE()),
    [Views] int NOT NULL DEFAULT 0,
    CONSTRAINT [PK_Posts] PRIMARY KEY ([Id])
);
```

名前を指定しなかった `Views` 列には `CONSTRAINT` 句が付いていない点に注目してください。列ごとに名前を付けて回るのが面倒な場合は、`UseNamedDefaultConstraints()` でモデル全体の既定値制約に EF Core が自動で名前を付けられます。

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.UseNamedDefaultConstraints();
}
```

これを有効にして同じモデルからマイグレーションを生成し直すと、SQL Server 向けの DDL で両方の列に名前が付きました。

```sql
[CreatedDate] datetime2 NOT NULL CONSTRAINT [DF_Posts_CreatedDate] DEFAULT (GETDATE()),
[Views] int NOT NULL CONSTRAINT [DF_Posts_Views] DEFAULT 0,
```

> [!WARNING]
> 既定値を設定した列には、**その型の CLR 既定値（`int` の `0`、`bool` の `false` など）を明示的に保存できません。** EF Core は「プロパティに値が設定されたかどうか」を CLR 既定値との比較で判定するため、`0` を代入することと何も代入しないことを区別できないからです。
>
> `Count` に `HasDefaultValue(-1)` を設定して 3 行を挿入したところ、次のようになりました。
>
> | 設定した内容 | 保存された値 |
> | --- | --- |
> | 何も設定しない | `-1` |
> | **`Count = 0` を明示** | **`-1`** |
> | `Count = 5` を明示 | `5` |
>
> 意図した `0` が黙って `-1` に化けます。この列に CLR 既定値を保存する必要がある場合は、プロパティを `int?` のような **null 許容型**にするか、バッキングフィールドを null 許容にしてください。null は CLR 既定値と区別できるため、`0` を明示的に保存できます。

> [!WARNING]
> 公式ドキュメントは「**既存のマイグレーションがある状態で `UseNamedDefaultConstraints()` を有効にすると、次に追加するマイグレーションでモデル内のすべての既定値制約がリネームされる**」と注意しています。稼働中のデータベースに対して有効化する場合は、生成されたマイグレーションの差分を必ず確認してください。

### EF Core ツールの使い分け

Visual Studio では、`dotnet ef` の代わりにパッケージマネージャーコンソールから PowerShell コマンドを使えます。また、複数のターゲットフレームワークを持つプロジェクトでは `--framework` の指定が必要です。

<details>
<summary>Visual Studio のパッケージマネージャーコンソールを使う場合</summary>

Visual Studio では、`Microsoft.EntityFrameworkCore.Tools` パッケージを追加すると、パッケージマネージャーコンソールから PowerShell コマンドを使用できます。

```powershell
Install-Package Microsoft.EntityFrameworkCore.Tools
```

以降、本章で紹介する `dotnet ef` コマンドは、次のように読み替えられます。

| .NET CLI | パッケージマネージャーコンソール |
| --- | --- |
| `dotnet ef migrations add <Name>` | `Add-Migration <Name>` |
| `dotnet ef database update` | `Update-Database` |
| `dotnet ef migrations script` | `Script-Migration` |
| `dotnet ef migrations remove` | `Remove-Migration` |

</details>

> [!IMPORTANT]
> EF Core 10 では、プロジェクトが `<TargetFramework>` ではなく **`<TargetFrameworks>`（複数形）で複数のフレームワークを対象にしている場合、`--framework` オプションの指定が必須** になりました。指定しないと `dotnet ef` は次のエラーで停止します（実測で確認済み）。
>
> ```text
> The project targets multiple frameworks. Use the --framework option to specify which target framework to use.
> ```
>
> ライブラリプロジェクトに `DbContext` を置いていて複数ターゲットにしている場合など、EF Core 9 から移行すると CI が突然失敗します。次のようにフレームワークを明示してください。
>
> ```bash
> dotnet ef migrations add AddPostPublishedAt --framework net10.0
> dotnet ef database update --framework net10.0
> ```

### SQL スクリプトとマイグレーションバンドルを使い分ける

SQL スクリプトは、DBA によるレビューやアーカイブが必要な場合に適しています。

```bash
# 空のデータベースから最新までのスクリプトを生成
dotnet ef migrations script

# 冪等 (idempotent) なスクリプトをファイルに出力
dotnet ef migrations script --idempotent --output artifacts/migrations.sql

# 特定のマイグレーション間のスクリプトを生成
dotnet ef migrations script AddNewTables AddAuditTable
```

`--idempotent` を付けると、各マイグレーションが未適用かどうかを確認してから実行するスクリプトが生成されるため、現在の適用状況が分からないデータベースにも安全に流せます。

自動デプロイには **マイグレーションバンドル** が推奨されます。公式ドキュメントによれば、バンドルは CI で生成でき、実行時に .NET SDK も EF Core ツールもアプリケーションのソースコードも不要で、**自己完結型にすれば .NET ランタイムすら不要**な単一の実行可能ファイルです。EF Core のマイグレーションロックも機能します。

```bash
# .NET ランタイムがインストール済みの環境向け
dotnet ef migrations bundle --output efbundle

# .NET ランタイムごと同梱する（Linux x64 向け）
dotnet ef migrations bundle --self-contained --target-runtime linux-x64 --output efbundle
```

生成したバンドルは、デプロイ先で実行可能ファイルとして起動します。

```bash
# デプロイ先で実行
./efbundle --connection "$CONNECTION_STRING"
```

> [!NOTE]
> 公式ドキュメントは、バンドルの制約として「SQL スクリプトと違い、実行される SQL を事前に確認したり、含まれるマイグレーションを一覧したりする手段が現時点ではない」と述べています。デプロイ前に SQL のレビューが必要な運用では、バンドルではなく `dotnet ef migrations script` でスクリプトを生成してください。

> [!TIP]
> EF Core 9 以降、マイグレーションの実行はデータベース全体のロックで保護されます（SQL スクリプトによる適用は除く）。これにより、複数のインスタンスが同時にマイグレーションを試みても、実行は 1 つに直列化されます。実際に SQL Server 2022 へ `dotnet ef database update` を実行すると、適用の前に次のメッセージが表示され、ロックの取得が行われていることが確認できます。
>
> ```text
> Acquiring an exclusive lock for migration application.
> See https://aka.ms/efcore-docs-migrations-lock for more information if this takes too long.
> ```

### マイグレーションバンドルの対象ランタイムを指定する

`dotnet ef migrations bundle` で対象ランタイムを指定するオプションは **`--target-runtime`（短縮形 `-r`）** です。よく似た `--runtime` というオプションも存在しますが、こちらは「ツールがビルドに使うランタイム」を指す別のオプションで、`--self-contained` と組み合わせると次のエラーで失敗することがあります（実測）。

```text
error NETSDK1047: 資産ファイル 'obj/project.assets.json' に 'net10.0/linux-x64' のターゲットがありません。
```

`--target-runtime` を使えば、プロジェクトに `RuntimeIdentifiers` を追加しなくても生成できます。実測では macOS 上から `--target-runtime linux-x64` でバンドルを生成し、Linux 向けの実行可能ファイル（ELF 64-bit）が出力されることを確認しました。生成したバンドルを実際に SQL Server 2022 に対して実行し、マイグレーションが適用されることも確認済みです。

### モデルとマイグレーションのずれを検出する

エンティティを変更したのにマイグレーションを追加し忘れると、モデルとデータベースのスキーマがずれます。**EF Core 9 以降、この状態で `Migrate()` / `MigrateAsync()` または `dotnet ef database update` を呼ぶと例外になります。** 公式は影響度が「High」の破壊的変更として扱っています。

SQL Server 2022 に対して、`Blog` に `Url` プロパティを足したままマイグレーションを作らずに `MigrateAsync()` を呼ぶと、次の例外になりました。

```text
InvalidOperationException: An error was generated for warning
'Microsoft.EntityFrameworkCore.Migrations.PendingModelChangesWarning':
The model for context 'PendContext' has pending changes.
Add a new migration before updating the database.
```

同じ状態で `dotnet ef database update` を実行した場合も、同じ警告 ID で失敗します。

#### コードから検出する

`DatabaseFacade.HasPendingModelChanges()` で同じ判定をコードから行えます。公式は「マイグレーションの追加を忘れたときに失敗する単体テストを書くのに使える」と述べています。

```csharp
using var db = new BloggingContext();

if (db.Database.HasPendingModelChanges())
{
    throw new InvalidOperationException("マイグレーションが追加されていません。");
}
```

#### CI で検出する

`dotnet ef migrations has-pending-model-changes` は、**保留中の変更があると終了コード 1 を返します**（実測）。ビルドパイプラインにそのまま組み込めます。

| 状態 | 出力 | 終了コード |
| --- | --- | --- |
| 保留中の変更あり | `Changes have been made to the model since the last migration. Add a new migration.` | 1 |
| 保留中の変更なし | `No changes have been made to the model since the last migration.` | 0 |

> [!WARNING]
> 例外メッセージが案内するとおり、この検査は `ConfigureWarnings` で抑制できます。
>
> ```csharp
> options.UseSqlServer(connectionString)
>        .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
> ```
>
> **しかし抑制してもモデルの変更が適用されるわけではありません。** 上と同じ条件で抑制して `MigrateAsync()` を実行したところ、呼び出しは成功したにもかかわらず、`Blogs` テーブルの列は `Id` と `Name` の 2 つのままで **`Url` 列は作られませんでした**。公式も、保留中の変更は「`Migrate` を呼んでも他のマイグレーションと一緒には適用されない」と述べています。抑制は問題を隠すだけで、その列を使うクエリは実行時に失敗します。

### 同時にマイグレーションが走らないようにする

起動時マイグレーションでいちばん怖いのは、**複数のインスタンスが同時に起動してマイグレーションを二重に適用する**ことです。コンテナーをスケールアウトした瞬間や、ローリングデプロイの最中に起こります。

公式ドキュメントによれば、EF Core 9 以降の `Migrate()` / `MigrateAsync()` は、マイグレーションを適用する前に**データベース全体のロックを自動で取得**します。ロックはマイグレーションの実行中（シードコードの実行も含む）保持され、完了すると自動的に解放されます。ロックは `dotnet ef database update`、`Update-Database`、マイグレーションバンドル、実行時マイグレーションのいずれにも適用されます。SQL スクリプトは EF Core の外で適用されるため対象外です。

SQL Server ではセッションレベルのアプリケーションロックが使われます。実際に `MigrateAsync()` が発行した SQL をログで拾うと、次の 2 本が確認できました（実測）。

```sql
DECLARE @result int;
EXEC @result = sp_getapplock @Resource = '__EFMigrationsLock', @LockOwner = 'Session', @LockMode = 'Exclusive';
SELECT @result

-- （マイグレーション適用後）
DECLARE @result int;
EXEC @result = sp_releaseapplock @Resource = '__EFMigrationsLock', @LockOwner = 'Session';
SELECT @result
```

このロックが本当に効くかを確かめるため、別の接続から先に `sp_getapplock` で同じリソース名を握った状態で `MigrateAsync()` を呼び、6 秒後にロックを解放してみました。

```text
先行ロック取得: 戻り値 0
6.0 秒後にロックを解放した
MigrateAsync 完了まで 6.1 秒（待たされた）
```

`MigrateAsync()` は**ロックが解放されるまで待ち**、解放された直後に処理を続けました。二重適用は起こりません。

#### マイグレーションを自分でトランザクションに包んではいけない

再試行を効かせるつもりで、次のように `MigrateAsync()` を実行戦略と明示的トランザクションで包むコードが以前は広く使われていました。

```csharp
// これは公式にサポートされない書き方
await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
{
    await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
    await db.Database.MigrateAsync(cancellationToken);
    await tx.CommitAsync(cancellationToken);
});
```

**公式ドキュメントは、`MigrateAsync` を明示的トランザクションで包むことを「サポートされない」と明記しています。** 理由は、外側でトランザクションを開始してしまうと**上で説明したデータベースロックを取得できなくなり、同時実行から保護されなくなる**ためです。EF Core 9 以降の `Migrate` / `MigrateAsync` は、必要なトランザクションと実行戦略を自分で管理します。

EF Core 9 ではこのパターンが `MigrationsUserTransactionWarning` による例外になりました。EF Core 10.0.11 で実際に上のコードを実行したところ、**例外にはならず、同じ警告 ID の警告がログに記録されました**（実測）。

```text
warn: RelationalEventId.MigrationsUserTransactionWarning[20412] (Microsoft.EntityFrameworkCore.Migrations)
```

例外にならなくても、ロックによる保護が失われることに変わりはありません。**外側のトランザクションと実行戦略は外して、`MigrateAsync()` をそのまま呼んでください。**

```csharp
await db.Database.MigrateAsync(cancellationToken);
```

> [!NOTE]
> EF Core 9 では、保留中のマイグレーションを**すべて 1 つのトランザクション**にまとめて適用する変更が入りましたが、公式の EF Core 10 のリリースノートによれば、この変更は「さまざまなマイグレーションのシナリオで問題を起こした」として **EF Core 10 で元に戻されました**。EF Core 10 では以前と同じく、マイグレーションごとにトランザクションが張られます。

#### SQLite では放置されたロックが残る

SQLite にはアプリケーションロックの仕組みがないため、公式ドキュメントによれば EF Core は代わりに **`__EFMigrationsLock` テーブル**を作成し、そこに行を挿入することでロックを表現します。実際にマイグレーションを適用した直後のテーブル一覧を見ると、次のようになっていました（実測）。

```text
テーブル: __EFMigrationsLock, __EFMigrationsHistory
```

問題は、公式ドキュメントも警告しているとおり、**マイグレーションの途中でプロセスが強制終了するとロック行が残ってしまう**ことです。この状態を手で再現し、その後もう一度マイグレーションを実行してみました。

```text
放置されたロック行を挿入した
8 秒経ってもロック待ちのまま（無期限に待つ）
ロックテーブルを削除したら完了した
```

タイムアウトはありません。**ロックが解放されるまで無期限に待ち続けます**。公式ドキュメントが示す解決方法は、`__EFMigrationsLock` テーブルを削除することです。次は SQLite で実測したときに使った SQL です。

```sql
DROP TABLE "__EFMigrationsLock";
```

> [!WARNING]
> 公式ドキュメントは「ロックの仕組みはプロバイダーによって大きく異なり、プロバイダー固有の問題を伴うことがある」と明記しています。使用するプロバイダーのドキュメントを必ず確認してください。
>
> また、`MigrateAsync()` を**明示的なトランザクションで囲むことはサポートされていません**。マイグレーションのトランザクションは EF Core 自身が管理します。

### 初期データの投入（シード）

マスターデータや動作確認用の初期データを投入する方法は 2 つあります。

1 つ目は `HasData` です。モデルの一部として宣言し、マイグレーションの中に `InsertData` として埋め込まれます。主キーを明示的に指定する必要があります。

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Blog>().HasData(
        new Blog { Id = 1, Name = "既定のブログ", Url = "https://example.com" });
}
```

`HasData` はマイグレーションの差分計算に組み込まれるため、値を書き換えると次のマイグレーションで `UpdateData` が生成されます。一方で、実行時にしか決まらない値（現在時刻や外部 API から取得する値）は扱えません。

2 つ目は EF Core 9 で追加された `UseSeeding` / `UseAsyncSeeding` です。こちらは通常の `DbContext` 操作としてデータを投入するため、任意のロジックを書けます。`MigrateAsync()` や `EnsureCreatedAsync()` の実行時に呼び出されます。

```csharp
builder.Services.AddDbContext<BloggingContext>(options =>
    options.UseSqlServer(connectionString)
           .UseSeeding((context, _) =>
           {
               if (!context.Set<Blog>().Any(b => b.Name == "既定のブログ"))
               {
                  context.Set<Blog>().Add(new Blog
                  {
                      Name = "既定のブログ",
                      Url = "https://example.com"
                  });
                  context.SaveChanges();
              }
           })
           .UseAsyncSeeding(async (context, _, cancellationToken) =>
           {
              if (!await context.Set<Blog>()
                      .AnyAsync(b => b.Name == "既定のブログ", cancellationToken))
              {
                  context.Set<Blog>().Add(new Blog
                  {
                      Name = "既定のブログ",
                      Url = "https://example.com"
                  });
                  await context.SaveChangesAsync(cancellationToken);
              }
           }));
```

> [!IMPORTANT]
> `UseSeeding` と `UseAsyncSeeding` は **両方を登録してください**。実際に SQLite で試したところ、`EnsureCreated()`（同期）では `UseSeeding` だけが呼ばれ、`EnsureCreatedAsync()`（非同期）では `UseAsyncSeeding` だけが呼ばれました。片方しか登録していないと、呼び出し側の API によってシードが実行されません。
>
> また、これらのデリゲートは毎回の実行で呼ばれる可能性があるため、上記のように **既に存在するかを確認してから追加** してください。この点は `HasData` と異なり、EF Core が重複を防いでくれません。

### 既存データベースからスキャフォールディングする

既存のデータベースからエンティティと `DbContext` を生成する `dotnet ef dbcontext scaffold` には、そのまま使うと危険な既定の挙動があります。

このコマンドをそのまま実行すると、**生成された `DbContext` の `OnConfiguring` に接続文字列がそのまま埋め込まれます**。SQL Server 2022 に対して実際に実行したところ、パスワードを含む接続文字列がソースコードに書き出され、あわせて次の `#warning` が生成されました。

```csharp
protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code.
    => optionsBuilder.UseSqlServer("Server=...;User Id=sa;Password=...");
```

公式ドキュメントは、これは「生成されたコードが最初に使うときにいきなり動かないという体験を避けるため」であり、**接続文字列が製品コードに存在してはならない**と明記しています。`--no-onconfiguring` オプションを付けると `OnConfiguring` の生成そのものを抑止でき、実測でも接続文字列を含まない、DI 用のコンストラクターだけを持つクラスが生成されました。

```bash
dotnet ef dbcontext scaffold "<接続文字列>" Microsoft.EntityFrameworkCore.SqlServer \
    --output-dir Models --no-onconfiguring
```

#### 既定値を持つ `bool` 列は `bool?` にならない

データベースファーストで運用していると、EF Core 8.0 での変更が生成結果に効いてきます。**既定値の制約を持つ NULL 非許容の `bit` 列は、以前は `bool?` として生成されていましたが、EF Core 8.0 以降は `bool` として生成されます。**

SQL Server 2022 に次のテーブルを作ってスキャフォールディングしました。

```sql
-- SQL Server
CREATE TABLE Articles (
  Id int IDENTITY PRIMARY KEY,
  Title nvarchar(200) NOT NULL,
  IsPublished bit NOT NULL CONSTRAINT DF_Articles_IsPublished DEFAULT 1
);
```

生成されたプロパティと構成は次のとおりでした（実測）。

```csharp
public bool IsPublished { get; set; }   // bool? ではない

entity.Property(e => e.IsPublished).HasDefaultValue(true, "DF_Articles_IsPublished");
```

以前 `bool?` にしていたのは、**`bool` の CLR 既定値が `false` のため、`false` を設定しても「未設定」と区別できず、データベースの既定値 `true` が入ってしまう**問題があったからです。EF Core 8.0 では、値が設定済みかどうかを判定する基準値（**センチネル**）を変更できるようになり、既定値が `true` の `bool` プロパティにはこれが自動で適用されます。実測でも `Sentinel = True` になっていました。

結果として、`false` と `true` で発行される INSERT が変わります（実測）。

```sql
-- SQL Server：IsPublished = false を設定した場合（値が送られる）
INSERT INTO [Articles] ([IsPublished], [Title])
OUTPUT INSERTED.[Id]
VALUES (@p0, @p1);

-- SQL Server：IsPublished = true を設定した場合（列が外れ、DB の既定値が使われて読み戻される）
INSERT INTO [Articles] ([Title])
OUTPUT INSERTED.[Id], INSERTED.[IsPublished]
VALUES (@p2);
```

> [!NOTE]
> 公式ドキュメントは、この変更が影響するのは「定期的にデータベースを再スキャフォールディングする データベースファーストのフロー」だけだと述べています。推奨される対応は、コード側を NULL 非許容の `bool` に合わせることです。どうしても以前の生成結果が必要な場合は、スキャフォールディングの T4 テンプレートを編集して元のマッピングに戻せます。

> [!WARNING]
> EF Core 8.0 では**生成されるナビゲーションの名前も変わることがあります**。以前は複合外部キーの列名に共通の接頭辞があるとそれをナビゲーション名に使っていましたが、この規則は廃止されました。公式は、この規則が `S` や `Student_`、ときには `_` だけといった「非常に貧弱な名前」を生むことがあったためだと説明しています。再スキャフォールディングでナビゲーション名が変わると既存のコードが壊れるため、差分を確認してください。

### 設計時 DbContext ファクトリ

`dotnet ef` コマンドは、設計時に `DbContext` のインスタンスを生成する必要があります。通常はアプリケーションの `Program.cs` からホストを構築して解決しますが、それが難しい構成（クラスライブラリにマイグレーションを置く場合など）では `IDesignTimeDbContextFactory<T>` を実装します。

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BloggingApi.Data;

public class BloggingContextFactory : IDesignTimeDbContextFactory<BloggingContext>
{
    public BloggingContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BloggingContext>();
        optionsBuilder.UseSqlServer(
            Environment.GetEnvironmentVariable("BLOGGING_CONNECTION")
                ?? @"Server=(localdb)\mssqllocaldb;Database=Blogging;Trusted_Connection=True");

        return new BloggingContext(optionsBuilder.Options);
    }
}
```

マイグレーションを別プロジェクトに置く場合は、コマンドで対象を指定します。ここで指定する 2 つのオプションは役割が異なります。公式ドキュメントによれば、`--project`（対象プロジェクト）は**生成されたファイルを受け取るプロジェクト**、`--startup-project`（スタートアッププロジェクト）は**ツールがビルドして実行するプロジェクト**です。ツールは接続文字列やモデルの構成を得るために、設計時にアプリケーションのコードを実行する必要があるためです。

```bash
dotnet ef migrations add InitialCreate \
    --project BloggingApi.Data \
    --startup-project BloggingApi.Data
```

> [!WARNING]
> **`--startup-project` に Web アプリケーションを指定すると、そちらにも `Microsoft.EntityFrameworkCore.Design` が必要になります。** 実際に Web アプリケーション側へパッケージを追加せずに実行すると、次のエラーで失敗します（実測）。
>
> ```text
> Your startup project 'BloggingApi' doesn't reference Microsoft.EntityFrameworkCore.Design.
> This package is required for the Entity Framework Core Tools to work.
> Ensure your startup project is correct, install the package, and try again.
> ```
>
> `Microsoft.EntityFrameworkCore.Design` は `<PrivateAssets>all</PrivateAssets>` 付きで追加されるため、**プロジェクト参照をたどって伝播しません**。クラスライブラリに入れても、スタートアッププロジェクト側には効きません。
>
> 公式ドキュメントは、この構成では**マイグレーションを持つプロジェクトを対象とスタートアップの両方に指定する**ことを推奨しています。そうすればツールがアプリケーションの起動コードを実行せずに済み、Web アプリケーション側に設計時パッケージを追加する必要もなくなります。上のコマンド例が両方を `BloggingApi.Data` にしているのはこのためです。

> [!TIP]
> マイグレーションを `DbContext` と**同じアセンブリ**に置いている限り、追加の構成は不要です。実行時にも `DbContext` のあるアセンブリからマイグレーションが自動的に発見されることを確認しています。
>
> 一方、`DbContext` とマイグレーションを**別々のプロジェクト**に分ける（データプロジェクトとマイグレーションプロジェクトを分離する）場合は、マイグレーションアセンブリの指定が必須です。指定せずに実行すると次のエラーになります（実測）。
>
> ```text
> Your target project 'BloggingApi.Migrations' doesn't match your migrations assembly 'BloggingApi.Data'.
> Either change your target project or change your migrations assembly.
> ```
>
> このときは、実行時と設計時の両方で次のように構成します。
>
> ```csharp
> options.UseSqlServer(
>     connectionString,
>     b => b.MigrationsAssembly("BloggingApi.Migrations"));
> ```
>
> なお公式ドキュメントは、**データプロジェクトからマイグレーションプロジェクトを参照してはならない**と明記しています。マイグレーションプロジェクトがすでにデータプロジェクトを参照しているため、循環参照になるからです。

> [!NOTE]
> **Spring Boot** では Flyway や Liquibase がマイグレーションを担い、SQL または XML/YAML でスキーマ変更を記述します。**Django** の `makemigrations` / `migrate` は、EF Core と同じく **モデルの差分を自動検出してマイグレーションファイルを生成する** 方式です。一方 **Laravel** の `php artisan make:migration` / `migrate` は、適用状況の管理とロールバックの仕組みこそ似ていますが、生成されるのは空のマイグレーションであり、`Schema` ファサードを使って変更内容を **自分で記述します**。モデルからの差分検出は行われません。

---

> [!TIP]
> **さらに詳しく**
>
> - [付録 EF Core 3：マイグレーションの詳細とクエリ](../appendix-efcore-03/index.md) — 生成されたマイグレーションの読み方、既定値制約の命名、同時実行の防止、シード

#### 複数のフレームワークを対象にしているプロジェクト

`TargetFrameworks` で複数のフレームワークを対象にしているプロジェクトでは、**EF Core 10 からどのフレームワークを使うかの指定が必須になりました。**

```text
The project targets multiple frameworks.
Use the --framework option to specify which target framework to use.
```

```bash
dotnet ef migrations add Init --framework net10.0
```

以前は EF Core が候補の中から 1 つを選んでいましたが、選ばれるフレームワークが意図と違うと分かりにくい失敗をするため、明示が求められるようになりました。ライブラリープロジェクトで複数フレームワークを対象にしている場合は、CI のスクリプトにも `--framework` を追加してください。

#### 生成されるナビゲーション名は EF Core 8 で変わった

既存のデータベースから生成されるナビゲーションの名前は、EF Core 8 で変わりました。以前は**複合外部キーの列名に共通の接頭辞があると、そこから名前を作る**ことがあり、`S` や `Student_`、ひどいときは `_` だけという名前が生成されていました。

現在はこの規則が廃止されています。実際に、複合主キーを持つ `Students` を `Sup_Id1` / `Sup_Id2` という列で参照する `Enrollments` を作ってスキャフォールディングしたところ、生成されたナビゲーション名は接頭辞の `Sup` ではなく、参照先の型名に基づく `Student` でした（実測）。

```csharp
// Enrollment.cs
public virtual Student Student { get; set; } = null!;

// Student.cs
public virtual ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
```

> [!WARNING]
> EF Core 7 以前で生成したコードを持つプロジェクトで再スキャフォールディングすると、**ナビゲーション名が変わってコードが壊れることがあります**。生成後の差分を必ず確認してください。名前を細かく制御したい場合、公式ドキュメントは T4 テンプレートによるカスタマイズを案内しています。

## 2. クエリの制御

### 単一クエリと分割クエリ

複数のコレクションナビゲーションを `Include` すると、EF Core は既定で 1 つの SQL に JOIN してまとめます。このとき、結果セットに同じ行が繰り返し現れる **カルテシアン爆発 (Cartesian Explosion)** が起こります。転送量そのものを減らす手段は[付録5の「インデックスを正しく張る」](/appendix-efcore-05/#インデックスを正しく張る)ではなく投影であり、判断の順序は[付録5の「まず計測する」](/appendix-efcore-05/#まず計測する)に従ってください。

```csharp
var blogs = await context.Blogs
    .Include(b => b.Posts)
    .Include(b => b.Contributors)
    .ToListAsync(cancellationToken);
```

Post が 10 件、Contributor が 10 件あるブログでは、JOIN の結果は 100 行になり、ブログの列がすべての行に重複して転送されます。

これを避けるには `AsSplitQuery()` を使い、コレクションごとに別々のクエリを発行させます。

```csharp
var blogs = await context.Blogs
    .Include(b => b.Posts)
    .Include(b => b.Contributors)
    .AsSplitQuery()
    .ToListAsync(cancellationToken);
```

アプリケーション全体の既定を分割クエリにすることもできます。

```csharp
builder.Services.AddDbContext<BloggingContext>(options =>
    options.UseSqlServer(connectionString,
        sqlOptions => sqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
```

個別のクエリで単一クエリに戻したい場合は `AsSingleQuery()` を呼びます。

| 観点 | 単一クエリ（既定） | 分割クエリ |
| --- | --- | --- |
| ラウンドトリップ数 | 1 回 | コレクションの数だけ増える |
| データ転送量 | 重複により増える可能性がある | 重複がない |
| 整合性 | 1 回のクエリなので一貫している | クエリ間で他のトランザクションによる変更が入り得る |

> [!WARNING]
> 分割クエリは既定では 1 つのトランザクションで実行されないため、クエリの合間に他のトランザクションがデータを変更すると、整合性のない結果になる可能性があります。整合性が重要な場合は明示的にトランザクションを開始するか、単一クエリを使ってください。なお 1 対 1 のナビゲーションは行が重複しないため、常に JOIN されます。

> [!NOTE]
> **EF Core 10 では、分割クエリの並べ替えの一貫性が修正されました。** EF Core 9 以前は、2 本目以降のクエリに埋め込まれるサブクエリの `ORDER BY` から主キー列が欠落することがあり、**検出しにくいデータ破損につながる可能性がありました**。
>
> 次のクエリを EF Core 10 で実行して、実際に発行された 2 本の SQL を確認しました。
>
> ```csharp
> var blogs = await context.Blogs
>     .AsSplitQuery()
>     .Include(b => b.Posts)
>     .OrderBy(b => b.Name)
>     .Take(2)
>     .ToListAsync();
> ```
>
> ```sql
> SELECT TOP(@p) [b].[Id], [b].[Name]
> FROM [Blogs] AS [b]
> ORDER BY [b].[Name], [b].[Id]
>
> SELECT [p].[Id], [p].[BlogId], [p].[Title], [b0].[Id]
> FROM (
>     SELECT TOP(@p) [b].[Id], [b].[Name]
>     FROM [Blogs] AS [b]
>     ORDER BY [b].[Name], [b].[Id]
> ) AS [b0]
> INNER JOIN [Post] AS [p] ON [b0].[Id] = [p].[BlogId]
> ORDER BY [b0].[Name], [b0].[Id]
> ```
>
> 2 本目のサブクエリでも `ORDER BY [b].[Name], [b].[Id]` と主キーまで含めて並べ替えられており、1 本目と同じ行が選ばれることが保証されています。EF Core 9 以前ではここが `ORDER BY [b].[Name]` だけだったため、`Name` が重複する行があると 1 本目と 2 本目で異なる行が選ばれ得ました。**分割クエリと `Take` / `Skip` を併用しているプロジェクトは、EF Core 10 へ上げる価値があります。**

### LeftJoin / RightJoin 演算子

.NET 10 では LINQ に `LeftJoin` と `RightJoin` の演算子が追加され、EF Core 10 はこれを SQL の `LEFT JOIN` / `RIGHT JOIN` に変換します。

```csharp
var results = await context.Students
    .LeftJoin(
        context.Departments,
        student => student.DepartmentId,
        department => department.Id,
        (student, department) => new
        {
            student.Name,
            DepartmentName = department != null ? department.Name : "所属なし"
        })
    .ToListAsync(cancellationToken);
```

従来は `SelectMany` と `GroupJoin`、`DefaultIfEmpty` を組み合わせる必要がありましたが、意図が明確に表現できるようになりました。

### 大文字小文字の区別は照合順序が決める

C# の `==` は大文字小文字を区別しますが、**SQL に翻訳された後は、データベースの照合順序 (collation) が区別するかどうかを決めます。** SQL Server の既定の照合順序は大文字小文字を区別しないため、次のクエリは `John` と `JOHN` の両方に一致しました。

```csharp
var count = await context.Customers.Where(c => c.Name == "john").CountAsync(cancellationToken);
// SQL: WHERE [c].[Name] = N'john'  →  実測で 2 件（John と JOHN）
```

EF Core は `==` を単に SQL の `=` に翻訳するだけで、大文字小文字の扱いを揃えようとはしません。これは意図的な設計です。そのため、`StringComparison` を受け取るオーバーロードは**翻訳できず例外になります。**

```csharp
// InvalidOperationException: The LINQ expression ... could not be translated.
context.Customers.Where(c => c.Name.Equals("john", StringComparison.OrdinalIgnoreCase))
```

クエリ単位で照合順序を指定したい場合は `EF.Functions.Collate` を使います。

```csharp
var exact = await context.Customers
    .Where(c => EF.Functions.Collate(c.Name, "SQL_Latin1_General_CP1_CS_AS") == "John")
    .CountAsync(cancellationToken);
// SQL: WHERE [c].[Name] COLLATE SQL_Latin1_General_CP1_CS_AS = N'John'  →  実測で 1 件
```

#### 照合順序を上書きするとインデックスが効かなくなる

これが最大の落とし穴です。インデックスは列の照合順序を引き継ぐため、**クエリで違う照合順序を指定すると照合順序が一致せず、インデックスが使えなくなります。** `Name` 列に非クラスター化インデックスを張った 2,001 行のテーブルで、SQL Server 2022 の実行プランを比較しました。

| クエリ | 実行プラン |
| --- | --- |
| `WHERE [Name] = N'John'` | `Index Seek(OBJECT:(...[IX_Customers_Name]))` |
| `WHERE [Name] COLLATE SQL_Latin1_General_CP1_CS_AS = N'John'` | `Clustered Index Scan(OBJECT:(...[PK_Customers]))` |
| `WHERE LOWER([Name]) = N'john'` | `Clustered Index Scan(OBJECT:(...[PK_Customers]))` |

インデックスシークが**全件走査に落ちています。** `ToLower()` や `ToUpper()` で大文字小文字を吸収する書き方も同じ結果になります。行数が増えるほど差は開きます。

> [!WARNING]
> 大文字小文字の区別を変えたいなら、**クエリではなく列またはデータベースの照合順序として定義してください。** そうすればすべてのクエリが暗黙にその照合順序を使い、インデックスの恩恵も受けられます。公式も「大量のデータを扱う性能上重要なクエリでは、必ず実行プランを確認し、適切なインデックスが使われているか確かめること」と警告しています。

```csharp
// 列の照合順序として定義する（この列に対するすべてのクエリに適用される）
modelBuilder.Entity<Customer>()
    .Property(c => c.Name)
    .UseCollation("SQL_Latin1_General_CP1_CS_AS");
```

#### null に対する ToString() は空文字になる

クエリの中で `ToString()` を使うと、EF Core はそれをデータベース側の文字列変換に翻訳します。このとき**値が `null` だったらどうなるか**は、EF Core 9 で統一されました。以前はデータ型や書き方によって `null` を返したり `"True"` を返したりとばらばらでしたが、**現在はどの場合も空文字列を返します**。

```csharp
var q = db.Things.Select(x => new { F = x.Flag.ToString(), N = x.Num.ToString() });
```

`bool?` と `int?` に `null` を入れた行を含めて実行すると、次の SQL に翻訳され、結果は空文字列になりました（SQL Server で実測）。

```sql
SELECT CASE [t].[Flag]
    WHEN CAST(0 AS bit) THEN N'False'
    WHEN CAST(1 AS bit) THEN N'True'
    ELSE N'' END AS [F],
    COALESCE(CONVERT(varchar(11), [t].[Num]), '') AS [N]
FROM [Things] AS [t]
```

```text
Flag='' (null? False)  Num='' (null? False)
Flag='True'            Num='5'
```

**返ってくるのは `null` ではなく空文字列**である点に注意してください。`== null` での判定は成立しません。以前の動作に戻したい場合は、公式ドキュメントが示すとおりクエリを書き換えます。

```csharp
db.Things.Select(x => x.Flag == null ? null : x.Flag.ToString());
```

#### 文字列の主キーは大文字小文字を区別せずに比較される

照合順序の話にはもう 1 つ落とし穴があります。EF Core 8 以降、SQL Server / Azure SQL プロバイダーでは、**文字列の主キーや外部キーの値を .NET 側でも大文字小文字を区別せずに比較します**。データベース側の既定の照合順序に合わせるための変更です。

つまり、`abc` を追跡している状態で `ABC` という別のインスタンスを追跡させようとすると、**同じキーとみなされて失敗します**。

```csharp
db.Attach(new Customer { Id = "abc", Name = "小文字" });
db.Attach(new Customer { Id = "ABC", Name = "大文字" }); // ここで例外
```

```text
InvalidOperationException: The instance of entity type 'Customer' cannot be tracked
because another instance with the same key value for {'Id'} is already being tracked.
```

実測でもこの例外が発生しました。データベースに問い合わせる前の、**変更追跡の段階で弾かれる**点が重要です。

大文字小文字を区別したいのであれば、公式ドキュメントが示すとおり、キーのプロパティに**プロバイダー値比較子 (provider value comparer)** を明示的に構成します。

```csharp
modelBuilder.Entity<Customer>()
    .Property(c => c.Id)
    .Metadata.SetProviderValueComparer(
        new ValueComparer<string>(
            (l, r) => string.Equals(l, r, StringComparison.Ordinal),
            v => v.GetHashCode()));
```

> [!WARNING]
> データベース側の列の照合順序も合わせて大文字小文字を区別するものに変更しないと、.NET 側とデータベース側で判定が食い違います。**片方だけを変えてはいけません。**

#### 文字列の主キーは EF Core 側でも大文字小文字を区別しない

照合順序の話はデータベース側の比較でしたが、**EF Core が変更追跡でキー値を突き合わせるときの比較**も同じ考え方に合わせられています。EF Core 8.0 以降、SQL Server / Azure SQL プロバイダーでは、文字列のキー値が **.NET の大文字小文字を区別しない序数比較子**で比較されます。それ以前は大文字小文字を区別する比較子でした。

公式ドキュメントはこの変更の理由を、「SQL Server は既定で、外部キーの値が主キーの値に一致するかを大文字小文字を区別せずに比較する。EF が大文字小文字を区別して比較すると、**本来つながるはずの外部キーと主キーが結び付かないことがある**」と説明しています。

実際に文字列キーを持つエンティティで確認したところ、キーの比較子は `CaseInsensitiveValueComparer` になり、`"ABC"` を主キーに持つ追跡中のエンティティを小文字の `"abc"` で検索して見つけられました（実測）。

```text
キーの ValueComparer            = CaseInsensitiveValueComparer
comparer.Equals("ABC", "abc")   = True
```

区別が必要な場合は、`ValueComparer` を明示的に設定します。

```csharp
var comparer = new ValueComparer<string>(
    (l, r) => string.Equals(l, r, StringComparison.Ordinal),
    v => v.GetHashCode(),
    v => v);

modelBuilder.Entity<Blog>().Property(e => e.Id).Metadata.SetValueComparer(comparer);
```

> [!NOTE]
> ただし、比較子を大文字小文字を区別するものに変えても、**データベース側の照合順序は変わりません**。大文字小文字を厳密に区別したいなら、列の照合順序（`UseCollation` / `HasCollation`）とあわせて設計してください。

### 複数の列で並べ替えるキーセットページング

並べ替えのキーが 1 つでは一意にならない場合、キーセットページングの条件は**単純な `>` の連鎖では書けません**。次のように書くと、境界にある行が丸ごと欠落します。

```csharp
// 誤り: 同じ Date の行が残っていても次の日付へ飛んでしまう
.Where(p => p.Date > lastDate)
```

日付が重複するデータ 6 件（`2026-01-01` が 3 件、`2026-01-02` が 3 件）を用意し、`Date` と `Id` の昇順で 2 件ずつページングして実測しました。1 ページ目は `Id=1, 2` で、境界は `Date=2026-01-01, Id=2` です。

| 条件 | 生成される `WHERE` 句 | 2 ページ目の結果 |
| --- | --- | --- |
| `p.Date > lastDate` | `[p].[Date] > @lastDate` | `Id=4, 5` — **`Id=3` が欠落** |
| 公式の OR パターン | `[p].[Date] > @lastDate OR ([p].[Date] = @lastDate AND [p].[Id] > @lastId)` | `Id=3, 4` — 正しい |

公式ドキュメントが示す正しい書き方は、**最後のキー以外が等しい場合を `OR` でつなぐ**形です。

```csharp
var page = await context.Posts
    .AsNoTracking()
    .OrderBy(p => p.Date)
    .ThenBy(p => p.Id)
    .Where(p => p.Date > lastDate || (p.Date == lastDate && p.Id > lastId))
    .Take(pageSize)
    .ToListAsync(cancellationToken);
```

並べ替えキーが増えるほど、この `OR` の項も増えていきます。

> [!WARNING]
> 多くの SQL データベースは、これをより簡潔かつ効率的に書ける **行値 (row values)** 構文 `WHERE (Date, Id) > (@lastDate, @lastId)` をサポートしていますが、**EF Core は現時点でこれを LINQ で表現できません**。公式ドキュメントにもその旨が明記されており、[dotnet/efcore#26822](https://github.com/dotnet/efcore/issues/26822) で追跡されています。EF Core 10 の時点でもこの Issue は Backlog のままです。
>
> 実際に `ValueTuple.Create(p.Date, p.Id).CompareTo(...)` の形で書いて実行したところ、SQL に変換できず `InvalidOperationException`（`The LINQ expression ... could not be translated`）になりました。上記の `OR` パターンを使ってください。

> [!TIP]
> ページングでは、**並べ替えに対応するインデックスが性能を左右します**。公式ドキュメントも「ページングの並べ替えに対応するインデックスを用意すること」を求めており、複数の列で並べ替える場合はそれらをまとめた**複合インデックス (composite index)** を定義します。

> [!IMPORTANT]
> ページングでは、並び順が一意になるようにしてください。並び順が同値の行があると、ページ間で行の順序が不定になります。上の例のように、末尾に主キーを加えるのが確実です。

> [!WARNING]
> `pageNumber` と `pageSize` をクエリ文字列などの外部入力から受け取る場合は、**必ず範囲を検証してください**。EF Core は値の妥当性を検査せず、そのまま SQL のページング句に渡します。そして **不正な値を渡したときの結果はデータベースによって異なります。**
>
> SQL Server の `OFFSET` は「0 以上」、`FETCH NEXT` は「1 以上」であることが T-SQL の仕様で定められています。したがって範囲外の値はエラーになります。一方 SQLite の `LIMIT` / `OFFSET` はエラーにならず、負の `LIMIT` は「上限なし」として扱われます。20 件のデータに対して実測した結果は次のとおりです。
>
> | 入力 | 生成される句 | SQL Server 2022 | SQLite |
> | --- | --- | --- | --- |
> | `pageNumber=0`, `pageSize=10` | `Skip(-10).Take(10)` | `SqlException`（下記） | 10 件（負の `OFFSET` が無視される） |
> | `pageNumber=1`, `pageSize=-5` | `Skip(-10).Take(-5)` | `SqlException`（下記） | **全 20 件が返る** |
> | `pageNumber=1`, `pageSize=0` | `Skip(0).Take(0)` | 0 件 | 0 件 |
>
> SQL Server 側の例外メッセージはそれぞれ次のとおりです。
>
> ```text
> The offset specified in a OFFSET clause may not be negative.
> The number of rows provided for a FETCH clause must be greater then zero.
> ```
>
> つまり **SQL Server では 500 エラーになり、SQLite では件数制限が外れてテーブル全体が返ります。** 後者は一覧 API がそのままサービス拒否や情報漏洩の窓口になるため、より危険です。いずれにせよ入力を信用せず、次のように上限つきで丸めてください。
>
> ```csharp
> pageNumber = Math.Max(pageNumber, 1);
> pageSize = Math.Clamp(pageSize, 1, 100);
> ```
>
> なお `Take(0)` はどちらのデータベースでも 0 件です。SQL Server では EF Core がページング句を生成せず `WHERE 0 = 1` に置き換えるため、データベースへの問い合わせ自体が最適化されます。
>
> ASP.NET Core のモデルバインディングを使う場合は、[第 3 章](../03-mvc-web-and-api/index.md)で扱った検証属性（`[Range(1, 100)]` など）を DTO に付けて、コントローラーに入る前に弾くのが確実です。

> [!TIP]
> **さらに詳しく**
>
> - [付録 EF Core 3：マイグレーションの詳細とクエリ](../appendix-efcore-03/index.md) — 単一クエリと分割クエリ、照合順序、生の SQL、ユーザー定義関数とビュー
> - [付録 EF Core 5：パフォーマンス](../appendix-efcore-05/index.md) — インデックス設計、コンパイル済みクエリ、NativeAOT

### 関連データを読み込まずに数える

本編で扱った明示的読み込み (`Collection(...).LoadAsync()`) は、関連データを**すべて**メモリに読み込みます。件数を数えたいだけの場合や、条件に合うものだけが欲しい場合は `Query()` を使います。`Query()` はそのナビゲーションに対応する `IQueryable` を返すので、後ろに LINQ を続けられます。

```csharp
var blog = await context.Blogs.SingleAsync(b => b.Id == id, cancellationToken);

// 件数だけを数える。Post のインスタンスは 1 件も作られない
var postCount = await context.Entry(blog)
    .Collection(b => b.Posts)
    .Query()
    .CountAsync(cancellationToken);

// 条件に合うものだけを読み込む
var goodPosts = await context.Entry(blog)
    .Collection(b => b.Posts)
    .Query()
    .Where(p => p.Rating > 3)
    .ToListAsync(cancellationToken);
```

SQL Server 2022 で実測すると、前者は次の SQL になり、`blog.Posts` は空のままでした。

```sql
-- SQL Server
SELECT COUNT(*)
FROM [Posts] AS [p]
WHERE [p].[BlogId] = @p
```

後者は `WHERE` に条件が積まれ、返ってきた 2 件だけが追跡されます。

```sql
-- SQL Server
SELECT [p].[Id], [p].[BlogId], [p].[Rating], [p].[Title]
FROM [Posts] AS [p]
WHERE [p].[BlogId] = @p AND [p].[Rating] > 3
```

参照ナビゲーション（単一の相手）を明示的に読み込むときは `Collection` ではなく `Reference` を使います。

```csharp
await context.Entry(post)
    .Reference(p => p.Blog)
    .LoadAsync(cancellationToken);
```

> [!NOTE]
> `Collection(...)` や `Reference(...)` が返すエントリーには `IsLoaded` プロパティがあります。公式リファレンスでは「そのナビゲーションが参照するエンティティが読み込まれていると**わかっている**かどうか」と説明されており、`Include` や `Load` / `LoadAsync` がこのフラグを立てます。フラグが立っている状態で再度 `LoadAsync` を呼んでも何も起きません (no-op)。
>
> 逆に、**関連エンティティがすべて読み込まれていても `IsLoaded` が `false` のままになることがあります**。読み込まれ方によっては「全部そろっている」と判断できないためです。実際、上の `Query().Where(...)` で読み込んだ場合、2 件が追跡された後も `IsLoaded` は `false` のままでした。確実にすべてを読み込みたいときは `LoadAsync` を呼びます。

### 常に Include する（AutoInclude）

特定のナビゲーションを「いつ取得しても必ず一緒に読み込む」とモデル側で決められます。

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Blog>().Navigation(b => b.Theme).AutoInclude();
}
```

こう構成すると、`Include` を書かなくても JOIN が入ります。SQL Server 2022 で `db.Blogs` の生成 SQL を確認したところ、次のようになりました。

```sql
-- SQL Server
SELECT [b].[Id], [b].[Name], [b].[ThemeId], [t].[Id], [t].[Color]
FROM [Blogs] AS [b]
LEFT JOIN [Theme] AS [t] ON [b].[ThemeId] = [t].[Id]
```

公式ドキュメントは、この構成が「結果に含まれるすべてのエンティティに対して適用される」と述べています。つまり、別のエンティティの `Include` の結果としてぶら下がってきた `Blog` にも `Theme` の読み込みが付いてきます。

特定のクエリでだけ読み込みたくない場合は `IgnoreAutoIncludes()` を使います。

```csharp
var blogs = await db.Blogs.IgnoreAutoIncludes().ToListAsync();
```

```sql
-- SQL Server
SELECT [b].[Id], [b].[Name], [b].[ThemeId]
FROM [Blogs] AS [b]
```

> [!WARNING]
> **所有型へのナビゲーションは `IgnoreAutoIncludes()` では外れません。** 所有型は規約によって自動読み込みに構成されますが、公式ドキュメントは「`IgnoreAutoIncludes` API を使っても含まれることは止められず、クエリ結果に含まれ続ける」と明記しています。実際に所有型 `Address` を持つ `Blog` で試したところ、`IgnoreAutoIncludes()` を付けても `[b].[Address_City]` は SELECT に残りました。

> [!NOTE]
> `AutoInclude` は書き忘れを防げる一方で、**そのエンティティを取得するすべてのクエリに JOIN のコストを課します**。一覧表示のように関連データが不要な画面でも必ず JOIN が入るため、既定では設定せず、必要なクエリで `Include` を書くほうが挙動を追いやすくなります。

### null の比較は C# と SQL で意味が違う

SQL のデータベースは比較を **3 値論理** (`true` / `false` / `null`) で扱いますが、C# は 2 値のブール論理です。EF Core は LINQ を SQL に変換するとき、この差を埋めるために追加の null チェックを補います。

次のエンティティを SQL Server 2022 で実測しました。`String1` と `String2` はどちらも null を許容します。

```csharp
var q = await context.Entities
    .Where(e => e.String1 != e.String2)
    .ToListAsync(cancellationToken);
```

生成される SQL には、C# と同じ意味になるよう補正が入ります。

```sql
-- SQL Server
SELECT [e].[Id], [e].[String1], [e].[String2]
FROM [Entities] AS [e]
WHERE ([e].[String1] <> [e].[String2] OR [e].[String1] IS NULL OR [e].[String2] IS NULL)
  AND ([e].[String1] IS NOT NULL OR [e].[String2] IS NOT NULL)
```

一方 `==` の場合はもっと単純です。**`!=` は `==` より複雑で遅くなりがち**なので、書き換えられるなら等価比較を使ってください。

```sql
-- SQL Server
SELECT [e].[Id], [e].[String1], [e].[String2]
FROM [Entities] AS [e]
WHERE [e].[String1] = [e].[String2] OR ([e].[String1] IS NULL AND [e].[String2] IS NULL)
```

null を明示的に除外しておくと、EF Core はその列を null 非許容として扱えるため SQL が単純になります。

```csharp
var q = await context.Entities
    .Where(e => e.String1 != null && e.String2 != null && e.String1 != e.String2)
    .ToListAsync(cancellationToken);
```

```sql
-- SQL Server
SELECT [e].[Id], [e].[String1], [e].[String2]
FROM [Entities] AS [e]
WHERE [e].[String1] IS NOT NULL AND [e].[String2] IS NOT NULL AND [e].[String1] <> [e].[String2]
```

`UseRelationalNulls(true)` を指定すると、この補正を無効にして SQL 本来の null の扱いをそのまま使えます。

```csharp
options.UseSqlServer(connectionString, o => o.UseRelationalNulls(true));
```

> [!WARNING]
> `UseRelationalNulls(true)` を使うと、**LINQ クエリの意味が C# と一致しなくなります**。上と同じデータ（4 行、うち `String1` か `String2` が null の行が 2 行）で実測したところ、既定では 2 件返る `String1 != String2` が、`UseRelationalNulls(true)` では 1 件しか返りませんでした。生成される SQL は `WHERE [e].[String1] <> [e].[String2]` だけになり、null を含む行が `WHERE` で落ちるためです。公式ドキュメントでも「期待と異なる結果になることがあるため、このモードの使用には注意が必要」と警告されています。

> [!TIP]
> null 非許容の列どうしの比較がもっとも単純で高速です。可能な場合は列を null 非許容にすることを検討してください。

### エンティティをそのまま JSON にすると循環参照で失敗する

EF Core はナビゲーションプロパティを自動的に補完 (fix-up) するため、**オブジェクトグラフに循環ができます**。`Blog` を `Include` で読み込むと `Blog.Posts` に `Post` が入り、その `Post.Blog` が元の `Blog` を指すためです。公式ドキュメントは、この循環をシリアル化フレームワークが扱えない場合があると明記しています。

実際に ASP.NET Core 10 の最小 API から、`Include` した `Blog` をそのまま `System.Text.Json` でシリアル化したところ、次の例外が発生しました。

```text
System.Text.Json.JsonException: A possible object cycle was detected. This can either be
due to a cycle or if the object depth is larger than the maximum allowed depth of 64.
Path: $.Posts.Blog.Posts.Blog.Posts.Blog....
```

対処は 3 つあります。1 つ目は、`ReferenceHandler.IgnoreCycles` を指定して循環部分を `null` に置き換える方法です。最小 API では `ConfigureHttpJsonOptions`、MVC やコントローラーでは `AddControllers().AddJsonOptions(...)` で設定します。

```csharp
builder.Services.ConfigureHttpJsonOptions(
    options => options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);
```

同じデータで実測した結果は次のとおりで、循環している `blog` が `null` になります。

```json
{"id":1,"name":"A","posts":[{"id":1,"title":"P1","blogId":1,"blog":null}]}
```

2 つ目は `ReferenceHandler.Preserve` です。こちらは循環を `$id` と `$ref` の参照に置き換えます。

```json
{"$id":"1","Id":1,"Name":"A","Posts":{"$id":"2","$values":[
  {"$id":"3","Id":1,"Title":"P1","BlogId":1,"Blog":{"$ref":"1"}}]}}
```

3 つ目は、循環の原因になっているナビゲーションプロパティに `System.Text.Json.Serialization` 名前空間の `[JsonIgnore]` を付けて、シリアル化の対象から外す方法です。

> [!WARNING]
> `ReferenceHandler.Preserve` は **JSON の形自体を変えます**。上の実測結果のとおり、配列だった `Posts` が `$id` と `$values` を持つオブジェクトになりました。クライアント側も参照形式を解釈できる必要があるため、公開 API のレスポンスに使うと互換性の問題を起こします。

> [!TIP]
> そもそも API のレスポンスにエンティティを直接使わず、DTO に投影すれば循環は発生しません。詳しくは[第8章の投影による最適化](/08-entity-framework-core/#投影-projection-による最適化)を参照してください。

## 3. SQL を直接扱う

### 生の SQL を使う

LINQ で表現できないクエリや、ストアドプロシージャの呼び出しには生の SQL を使います。生の SQL と `SaveChanges` を 1 つのトランザクションにまとめる場合は[付録4の「セーブポイント」](/appendix-efcore-04/#セーブポイント)と[「接続の回復性とトランザクションの併用」](/appendix-efcore-04/#接続の回復性とトランザクションの併用)もあわせて確認してください。

```csharp
var blogs = await context.Blogs
    .FromSql($"SELECT * FROM [Blogs] WHERE [Url] LIKE {pattern}")
    .AsNoTracking()
    .ToListAsync(cancellationToken);
```

`FromSql` は **補間文字列 (FormattableString)** を受け取り、埋め込まれた値を自動的に SQL パラメーターに変換します。文字列としてそのまま連結されるわけではないため、SQL インジェクションの心配がありません。実際に `pattern` に `https://ok.example.com' OR '1'='1` を渡して試したところ、発行される SQL は次のようになり、値はパラメーターとして扱われて 0 件が返りました。

```text
.param set p0 'https://ok.example.com'' OR ''1''=''1'
SELECT * FROM Blogs WHERE Url = @p0
```

> [!WARNING]
> `FromSql` でエンティティ型を返すには、次の 2 つを満たす必要があります。
>
> - SQL が、そのエンティティ型の**すべてのプロパティ分のデータを返す**こと
> - 結果セットの**列名が、プロパティのマップ先の列名と一致する**こと
>
> どちらを外しても同じ実行時エラーになります。`Blog` が `Id` / `Name` / `Owner` を持つとき、`Owner` を落とした SQL と、`Name` に別名を付けた SQL の両方を試したところ、次のようになりました。
>
> ```csharp
> // どちらも InvalidOperationException
> await context.Blogs.FromSql($"SELECT [Id], [Name] FROM [Blogs]").ToListAsync();
> await context.Blogs.FromSql($"SELECT [Id], [Name] AS BlogName, [Owner] FROM [Blogs]").ToListAsync();
> ```
>
> ```text
> InvalidOperationException: The required column 'Owner' was not present in the results of a 'FromSql' operation.
> InvalidOperationException: The required column 'Name' was not present in the results of a 'FromSql' operation.
> ```
>
> `SELECT *` を使っていれば普通は問題になりませんが、列を絞ったり別名を付けたりすると起きます。列を絞りたい場合は、次に説明する未マップ型を使ってください。

> [!WARNING]
> **パラメーター化は SQL インジェクションを防ぎますが、`LIKE` のワイルドカードは防ぎません。** T-SQL の仕様では `%` は「0 文字以上の任意の文字列」に一致します。上の例の `pattern` を外部入力から受け取っている場合、利用者が `%` だけを渡すと `LIKE '%'` として解釈され、**テーブルの全行が返ります**。実際に試すと、`FromSql` でも `EF.Functions.Like` でも同じく全行が返りました。
>
> ```csharp
> // pattern = "%" のとき、どちらも全行が返る
> await context.Blogs.FromSql($"SELECT * FROM [Blogs] WHERE [Url] LIKE {pattern}").ToListAsync();
> await context.Blogs.Where(b => EF.Functions.Like(b.Url, pattern)).ToListAsync();
> ```
>
> 一方、`string.Contains` を使った場合は EF Core がメタ文字をエスケープするため、`Contains("%")` は「文字としての `%` を含む行」を探し、0 件になります。
>
> ```csharp
> // こちらは "%" を文字として扱う
> await context.Blogs.Where(b => b.Url.Contains(pattern)).ToListAsync();
> ```
>
> 部分一致検索を外部入力で行う場合は、`Contains` / `StartsWith` / `EndsWith` を使うか、`LIKE` を使うなら `%` と `_` を自分でエスケープしてください。大量データに対する `LIKE '%...'` は全表スキャンを誘発するため、性能面でも入力の検証が必要です。

エンティティ型を返さないスカラークエリには `SqlQuery` を使います。

```csharp
var ids = await context.Database
    .SqlQuery<int>($"SELECT [Id] FROM [Blogs] WHERE [CreatedAt] > {threshold}")
    .ToListAsync(cancellationToken);
```

> [!IMPORTANT]
> `SqlQuery` の結果に LINQ 演算子を続けて適用する場合、EF Core は指定した SQL をサブクエリとして包み、その出力列を `Value` という名前で参照します。そのため、**出力列に `AS [Value]` という別名を付ける必要があります**。別名を付けずに `FirstAsync` や `Where` を続けると、「`Value` という列がない」という実行時エラーになります。
>
> ```csharp
> // LINQ を合成する場合は AS [Value] が必要
> var recentIds = await context.Database
>     .SqlQuery<int>($"SELECT [Id] AS [Value] FROM [Blogs] WHERE [CreatedAt] > {threshold}")
>     .Where(id => id > 100)
>     .ToListAsync(cancellationToken);
> ```

#### 未マップ型 (DTO) を直接受け取る

`SqlQuery` が扱えるのはスカラー値だけではありません。**EF Core のモデルに含まれていない任意の CLR 型**にも結果を詰められます（EF Core 8.0 で追加）。複数のテーブルを結合した結果や、列の部分集合をそのまま DTO に受け取れるため、生の SQL を書くときに `DbCommand` などの低レベルな API へ降りる必要がなくなります。

```csharp
public class PostSummary
{
    public string BlogName { get; set; } = "";
    public string PostTitle { get; set; } = "";
    public int Rating { get; set; }
}
```

```csharp
var summaries = await context.Database
    .SqlQuery<PostSummary>(
        $"""
        SELECT b.[Name] AS BlogName, p.[Title] AS PostTitle, p.[Rating]
        FROM [Posts] AS p INNER JOIN [Blogs] AS b ON p.[BlogId] = b.[Id]
        """)
    .ToListAsync(cancellationToken);
```

型はデータベースのどのテーブルとも一致している必要がありません。パラメーター付きコンストラクターや `[Column]` 属性といった、EF Core が対応するマッピング機構もそのまま使えます。結果は変更追跡されないため、実測でも `ChangeTracker.Entries()` は 0 件でした。

> [!NOTE]
> スカラーの `SqlQuery` と違い、`AS [Value]` は不要です。EF Core は指定した SQL をサブクエリとして包み、**プロパティ名と同じ名前の列**を参照するためです。そのまま LINQ を合成でき、実際に `Where` を続けたところ次の SQL が発行されました。
>
> ```sql
> SELECT [p].[BlogName], [p].[PostTitle], [p].[Rating]
> FROM (
>     SELECT b.[Name] AS BlogName, p.[Title] AS PostTitle, p.[Rating]
>     FROM [Posts] AS p INNER JOIN [Blogs] AS b ON p.[BlogId] = b.[Id]
> ) AS [p]
> WHERE [p].[Rating] >= 5
> ```

> [!WARNING]
> 未マップ型には **キーが定義されず、他の型へのリレーションシップも持てません。** ナビゲーションに見えるプロパティ（他のエンティティ型やそのコレクション）を含めると、実行時に次の例外になります。
>
> ```text
> InvalidOperationException: The property 'BadDto.Posts' of type 'List<Post>' appears to be
> a navigation to another entity type. Navigations are not supported when using 'SqlQuery".
> Either include this type in the model and use 'FromSql' for the query, or ignore this
> property using the '[NotMapped]' attribute.
> ```
>
> リレーションシップが必要な型はモデルにマップして `FromSql` を使うか、そのプロパティに `[NotMapped]` を付けてください。
>
> また、**型のプロパティに対応する列が結果セットに無い場合も実行時エラー**です。上の SQL から `PostTitle` の列だけを削って試したところ `The required column 'PostTitle' was not present in the results of a 'FromSql' operation.` になりました。逆に、結果セットに余分な列がある分には問題なく、余った列は無視されます。

ここまでの例は、生の SQL を書かずに LINQ の `Select` だけでも同じ結果が得られます。SQL を書く必要が本当にあるのかは、先に検討してください。

```csharp
// 上と同じ結果を LINQ だけで得る
var summaries = await context.Posts
    .Select(p => new PostSummary
    {
        BlogName = p.Blog!.Name,
        PostTitle = p.Title,
        Rating = p.Rating,
    })
    .Where(x => x.Rating >= 5)
    .ToListAsync(cancellationToken);
```

更新系の SQL は `ExecuteSqlAsync` です。

```csharp
await context.Database.ExecuteSqlAsync(
    $"UPDATE [Blogs] SET [Name] = {newName} WHERE [Id] = {id}",
    cancellationToken);
```

> [!WARNING]
> `FromSqlRaw` / `ExecuteSqlRaw` は文字列をそのまま SQL として扱うため、ユーザー入力を連結すると **SQL インジェクション** の脆弱性になります。可変値は必ずパラメーターとして渡し、どうしても `Raw` 系を使う場合は `new SqlParameter(...)` を明示的に指定してください。
>
> EF Core には、この誤りをコンパイル時に検出するアナライザーが 2 つあります。どちらもカテゴリーは **Security**、重大度は **Warning** です。
>
> | 診断 ID | 検出する書き方 | 導入バージョン |
> | --- | --- | --- |
> | `EF1002` | `FromSqlRaw` に **補間文字列** を渡す | EF Core 8 |
> | `EF1003` | `FromSqlRaw` に **連結した文字列** を渡す | EF Core 10 |
>
> 実際に両方の書き方を含むコードをビルドすると、次の 2 件が報告されます（実測で確認済み）。
>
> ```text
> warning EF1002: Method 'FromSqlRaw' inserts interpolated strings directly into the SQL,
> without any protection against SQL injection. Consider using 'FromSql' instead...
>
> warning EF1003: Method 'FromSqlRaw' inserts concatenated strings directly into the SQL,
> without any protection against SQL injection. Consider using 'FromSql' instead...
> ```
>
> **特に `EF1002` は見落としやすい落とし穴です。** `FromSql` に補間文字列を渡すと自動的にパラメーター化されるのに対し、`FromSqlRaw` に同じ見た目の補間文字列を渡すと、値が SQL に直接埋め込まれてしまいます。メソッド名を `FromSql` から `FromSqlRaw` に変えるだけで、安全なコードが危険なコードに変わるということです。
>
> CI では `<WarningsAsErrors>EF1002;EF1003</WarningsAsErrors>` を設定して、これらの警告をビルドエラーに昇格させておくと確実です。

```csharp
// 危険：絶対に書かない（EF1003 警告が出る）
var unsafeBlogs = await context.Blogs
    .FromSqlRaw("SELECT * FROM [Blogs] WHERE [Url] = '" + userInput + "'")
    .ToListAsync(cancellationToken);

// 危険：見た目は FromSql とほぼ同じだが値が埋め込まれる（EF1002 警告が出る）
var alsoUnsafeBlogs = await context.Blogs
    .FromSqlRaw($"SELECT * FROM [Blogs] WHERE [Url] = '{userInput}'")
    .ToListAsync(cancellationToken);

// 安全：パラメーター化する
var safeBlogs = await context.Blogs
    .FromSqlRaw("SELECT * FROM [Blogs] WHERE [Url] = {0}", userInput)
    .ToListAsync(cancellationToken);
```

#### 列名は動的にできない

パラメーターにできるのは**値だけ**です。公式ドキュメントは「データベースは列名（やスキーマのその他の部分）のパラメーター化を許可しない」と明記しています。次のように列名を補間しても意図どおりには動きません。

```csharp
var columnName = "Owner";
var columnValue = "johndoe";

// 動かない
var blogs = await context.Blogs
    .FromSql($"SELECT * FROM [Blogs] WHERE {columnName} = {columnValue}")
    .ToListAsync(cancellationToken);
```

> [!WARNING]
> **このコードは例外になりません。静かに間違った結果を返します。** SQL Server 2022 に対して実測したところ、生成された SQL は次のようになりました。
>
> ```sql
> DECLARE p0 nvarchar(4000) = N'Owner';
> DECLARE p1 nvarchar(4000) = N'johndoe';
>
> SELECT * FROM [Blogs] WHERE @p0 = @p1
> ```
>
> 列名まで文字列パラメーターになった結果、条件は `'Owner' = 'johndoe'` という**文字列同士の比較**になり、常に偽です。実際に該当データがあるにもかかわらず、返ってきた件数は **0 件**でした。例外が出ないぶん、テストデータでもたまたま 0 件だと気づけない、たちの悪い不具合になります。

どうしても列名を動的に組み立てる必要がある場合は、公式ドキュメントが示すとおり `FromSqlRaw` を使い、**列名は文字列補間で埋め込み、値は `DbParameter` として渡します。**

```csharp
var columnName = "Owner";
var columnValue = new SqlParameter("columnValue", "johndoe");

var blogs = await context.Blogs
    .FromSqlRaw($"SELECT * FROM [Blogs] WHERE {columnName} = @columnValue", columnValue)
    .ToListAsync(cancellationToken);
```

この形なら正しく 1 件が返りました（実測）。ただし前述のとおり `FromSqlRaw` に補間文字列を渡すため **`EF1002` の警告が出ます**。列名が安全な出所であることを確認したうえで、その箇所だけ警告を抑制してください。

> [!IMPORTANT]
> 公式ドキュメントは、実装方法の前に**そもそも動的に組み立てるべきかを考えるよう**促しています。列名をユーザーから受け取ると、**インデックスのない列を選ばれてクエリが極端に遅くなりデータベースに過負荷をかける**おそれや、**公開したくないデータを含む列を選ばれる**おそれがあります。本当に動的でなければならない場面を除き、**2 つの列名に対しては 2 つのクエリを書くほうがよい**というのが公式の助言です。

`FromSql` の結果には LINQ を続けて適用できるため、共通部分だけ SQL で書き、絞り込みや並べ替えは LINQ に任せるといった使い分けが可能です。

```csharp
var blogs = await context.Blogs
    .FromSql($"SELECT * FROM [Blogs] WHERE [Rating] > {minRating}")
    .Where(b => b.Url.Contains("dotnet"))
    .OrderBy(b => b.Name)
    .ToListAsync(cancellationToken);
```

#### 合成できる SQL には条件がある

ただし LINQ を続けられるのは、**渡した SQL が合成可能 (composable) な場合だけ**です。EF Core は与えられた SQL を**サブクエリとして扱う**ため、サブクエリに置けない構文が含まれていると失敗します。公式ドキュメントは合成できない例として次を挙げています。

| 書き方 | SQL Server 2022 での実測結果 |
| --- | --- |
| `SELECT * FROM [Blogs] WHERE ...` | 成功 |
| 末尾にセミコロンを付ける | `SqlException: Incorrect syntax near ';'.` |
| `TOP` や `OFFSET` を伴わない `ORDER BY` を含める | `SqlException: The ORDER BY clause is invalid in views, inline functions, derived tables, subqueries, and common table expressions, unless TOP, OFFSET or FOR XML is also specified.` |

このほか、SQL Server ではクエリレベルのヒント（`OPTION (HASH JOIN)` など）を末尾に付けた SQL も合成できません。**合成する予定の SQL は `SELECT` で始まり、末尾のセミコロンを付けないのが基本**だと覚えておいてください。

#### ストアドプロシージャを実行する

`FromSql` はストアドプロシージャの実行にも使えます。

```csharp
var blogs = await context.Blogs
    .FromSql($"EXECUTE dbo.GetPopularBlogs {minRating}")
    .ToListAsync(cancellationToken);
```

補間した値は通常の `FromSql` と同じように `DbParameter` に変換されるため、SQL インジェクションの心配はありません。省略可能なパラメーターがあるストアドプロシージャでは、`SqlParameter` を使って**名前付きパラメーター**の記法も書けます（実測で動作を確認しました）。

```csharp
var p = new SqlParameter("p", 5);

var blogs = await context.Blogs
    .FromSql($"EXECUTE dbo.GetPopularBlogs @minRating={p}")
    .ToListAsync(cancellationToken);
```

> [!WARNING]
> **SQL Server はストアドプロシージャの呼び出しに対する合成を許可しません。** つまり、前項の `Where` や `OrderBy` を続けることができません。実測では、`Where` を続けた時点で次の例外になりました。
>
> ```text
> InvalidOperationException: 'FromSql' or 'SqlQuery' was called with non-composable SQL
> and with a query composing over it. Consider calling 'AsEnumerable' after the method
> to perform the composition on the client side.
> ```
>
> 公式ドキュメントの指示どおり、`FromSql` の**直後に `AsEnumerable()` または `AsAsyncEnumerable()` を挟む**と、EF Core が合成を試みなくなり正しく動作します。ただしメッセージにあるとおり、その後の絞り込みは**クライアント側での処理**になります。データベースから返る行数が多いと無駄が大きいので、絞り込み条件はストアドプロシージャの引数として渡すほうが適切です。
>
> ```csharp
> var blogs = context.Blogs
>     .FromSql($"EXECUTE dbo.GetPopularBlogs {minRating}")
>     .AsEnumerable()
>     .Where(b => b.Name.Contains("dotnet"))
>     .ToList();
> ```

> [!NOTE]
> 公式ドキュメントは「渡すパラメーターはストアドプロシージャの定義と**厳密に一致していなければならない**」と注意しています。順序を間違えたり抜かしたりしないよう気をつけるか、上記の名前付きパラメーター記法を使ってください。型と、サイズ・精度・スケールなどの属性も対応させる必要があります。

結果セットを返さないストアドプロシージャは `ExecuteSql` で呼び出します。実測では、3 行を更新するストアドプロシージャの戻り値が影響行数の `3` になりました。

```csharp
var affected = await context.Database
    .ExecuteSqlAsync($"EXECUTE dbo.BumpRatings {delta}", cancellationToken);
```

> [!TIP]
> `FromSql` の結果は、通常の LINQ クエリと**まったく同じ変更追跡の規則**に従います。エンティティ型を返すクエリなら既定で追跡されるため、読み取り専用なら `AsNoTracking()` を付けてください。実測でも、ストアドプロシージャから 2 件取得した直後の `ChangeTracker.Entries()` は 2 でした。

### ユーザー定義関数とビューをマッピングする

EF Core の公式パフォーマンスガイダンスは、EF が生成しない最適な SQL を使いたい場合の手段を **3 つ**挙げています。1 つ目が前節の `FromSql` で、残りの 2 つが**ユーザー定義関数 (User-Defined Function: UDF)** と**データベースビュー**です。`FromSql` は「その 1 か所でしか使わない SQL」に向く一方、複数のクエリから再利用したいロジックは関数やビューにするほうが管理しやすくなります。ビューや集計結果を主キーのない型として読む方法は[付録2の「キーなしエンティティ型でビューや集計結果を読む」](/appendix-efcore-02/#キーなしエンティティ型でビューや集計結果を読む)で扱います。

#### スカラー関数

戻り値が単一の値である**スカラー関数**は、シグネチャだけを合わせた CLR メソッドを定義し、`HasDbFunction` でマッピングします。メソッドの本体は呼び出されないため、例外を投げておいて構いません。

```csharp
public class BloggingContext : DbContext
{
    public int PostCountForBlog(int blogId) => throw new NotSupportedException();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDbFunction(
            typeof(BloggingContext).GetMethod(nameof(PostCountForBlog), [typeof(int)])!);
    }
}
```

```csharp
var names = await context.Blogs
    .Where(b => context.PostCountForBlog(b.Id) > 1)
    .Select(b => b.Name)
    .ToListAsync(cancellationToken);
```

SQL Server 2022 に対して生成された SQL は次のとおりで、関数呼び出しがそのまま `WHERE` 句に埋め込まれました（実測）。

```sql
SELECT [b].[Name]
FROM [Blogs] AS [b]
WHERE [dbo].[PostCountForBlog]([b].[Id]) > 1
```

#### テーブル値関数 (TVF)

行の集合を返す**テーブル値関数 (Table-Valued Function: TVF)** は、`IQueryable<T>` を返す CLR メソッドとしてマッピングします。本体には `FromExpression` を書きます。

```csharp
public IQueryable<Post> PopularPosts(int likeThreshold)
    => FromExpression(() => PopularPosts(likeThreshold));

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.HasDbFunction(
        typeof(BloggingContext).GetMethod(nameof(PopularPosts), [typeof(int)])!);
}
```

`DbSet` と同じように扱えるため、**LINQ を合成できる**のが利点です。SQL Server での実測では、TVF に `Where` を続けた次のクエリが 1 本の SQL に変換されました。

```csharp
var titles = await context.PopularPosts(5)
    .Where(p => p.Title.StartsWith("a"))
    .Select(p => p.Title)
    .ToListAsync(cancellationToken);
```

```sql
SELECT [p].[Title]
FROM [dbo].[PopularPosts](@likeThreshold) AS [p]
WHERE [p].[Title] LIKE N'a%'
```

> [!NOTE]
> 公式ドキュメントは「クエリ可能な関数はテーブル値関数にマッピングしなければならない」と明記しています。また `HasTranslation` はスカラー関数専用で、テーブル値関数には使えません。

#### ビューとキーレスエンティティ型

パラメーターが不要なら、ビューをマッピングする方法もあります。公式ドキュメントは「関数と違い、**ビューはパラメーターを受け取れない**」とその違いを説明しています。ビューには主キーがないことが多いため、**キーレスエンティティ型 (keyless entity type)** として定義します。

```csharp
[Keyless]
public class BlogPostCount
{
    public string BlogName { get; set; } = "";
    public int PostCount { get; set; }
}

modelBuilder.Entity<BlogPostCount>()
    .HasNoKey()
    .ToView("View_BlogPostCounts");
```

```csharp
var counts = await context.BlogPostCounts
    .OrderBy(v => v.BlogName)
    .ToListAsync(cancellationToken);
```

キーレスエンティティ型は公式ドキュメントで「**DbContext で変更追跡されることが決してなく、したがって挿入・更新・削除もされない**」と定義されています。実際に上のクエリを実行した直後に `ChangeTracker.Entries()` を数えたところ **0** でした（実測）。`AsNoTracking()` を付け忘れる心配がありません。

> [!WARNING]
> **`ToView` でマッピングしたビューは、EF Core が作ってくれません。** `ToView` を呼ぶとそのエンティティ型は「テーブルにマップされていない」と扱われるため、マイグレーションの対象から外れます。実際に `GenerateCreateScript()` の出力を確認したところ、`CREATE TABLE` は 2 つ生成されましたが、ビューの DDL は含まれていませんでした（実測）。公式のサンプルも `ExecuteSqlRawAsync` でビューを作成しています。マイグレーションで管理するなら `migrationBuilder.Sql(...)` に `CREATE VIEW` を自分で書いてください。

> [!TIP]
> EF Core から見た `ToView` の対象は「読み取り専用のクエリソース」であり、公式ドキュメントによれば**実際にデータベースビューである必要はありません**。読み取り専用として扱いたい通常のテーブルを指定することもできます。

---

### SQL Server 固有の関数を LINQ から呼ぶ

`EF.Functions` には、プロバイダーごとの固有関数を LINQ から呼ぶための拡張メソッドが用意されています。SQL Server では日付差分や型判定などが翻訳されます。

```csharp
var q = db.Events.Where(e => EF.Functions.DateDiffDay(e.Start, e.End) > 10);
```

```sql
SELECT [e].[Id], [e].[End], [e].[Start], [e].[Title]
FROM [Evs] AS [e]
WHERE DATEDIFF(day, [e].[Start], [e].[End]) > 10
```

`DateDiffDay` のほかに `DateDiffMonth` や `DateDiffYear` など単位ごとのメソッドがあり、いずれも `DATEDIFF` の第 1 引数が変わるだけです。実測では 2026-01-01 から 2026-03-15 までが `DateDiffMonth` で `2` になりました（**日数ではなく境界をまたいだ回数**で数えます）。

`IsDate` は SQL Server の `ISDATE` に翻訳されます。

```sql
WHERE CAST(ISDATE([e].[Title]) AS bit) = CAST(1 AS bit)
```

> [!WARNING]
> `EF.Functions.Contains` と `EF.Functions.FreeText` は文字列の部分一致ではなく、**SQL Server の全文検索** に翻訳されます。対象の列に全文検索インデックスがないと、実行時に次の例外になります（実測）。
>
> ```text
> Cannot use a CONTAINS or FREETEXT predicate on table or indexed view 'Evs' because it is not full-text indexed.
> ```
>
> 部分一致がしたいだけなら `EF.Functions.Like` または `string.Contains` を使ってください。公式ドキュメントも、全文検索を使う前に全文検索カタログと全文検索インデックスを作成する必要があると明記しています。

## 4. 参考ドキュメント

- [マイグレーションの概要 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/managing-schemas/migrations/)
- [マイグレーションの適用 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/managing-schemas/migrations/applying)
- [単一クエリと分割クエリ | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/querying/single-split-queries)
- [関連データの一括読み込み | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/querying/related-data/eager)
- [関連データの明示的読み込み | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/querying/related-data/explicit)
- [クエリでの null 値の比較 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/querying/null-comparisons)
- [NavigationEntry.IsLoaded プロパティ | Microsoft Learn](https://learn.microsoft.com/ja-jp/dotnet/api/microsoft.entityframeworkcore.changetracking.navigationentry.isloaded?view=efcore-10.0)
- [関連データとシリアル化 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/querying/related-data/serialization)
- [照合順序と大文字小文字の区別 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/miscellaneous/collations-and-case-sensitivity)
- [SQL クエリ | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/querying/sql-queries)
- [マイグレーションの管理 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/managing-schemas/migrations/managing)
- [EF Core 9 の破壊的変更 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-9.0/breaking-changes)
- [EF Core 8 の破壊的変更 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/what-is-new/ef-core-8.0/breaking-changes)
- [ユーザー定義関数のマッピング | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/querying/user-defined-function-mapping)
- [SQL Server プロバイダーの関数マッピング | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/functions)
- [SQL Server プロバイダーの全文検索 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/full-text-search)
- [SQLite プロバイダーの関数マッピング | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sqlite/functions)
- [SQL Server プロバイダーのその他の考慮事項 | Microsoft Learn](https://learn.microsoft.com/ja-jp/ef/core/providers/sql-server/misc)
