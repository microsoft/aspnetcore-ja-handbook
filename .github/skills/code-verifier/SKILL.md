---
name: code-verifier
description: 'ドキュメントのコードサンプルを実装しテストで検証する。Use when: コードサンプルを検証する、サンプルコードをテストする、実装を確認する、コードが動作するか確認する。コードサンプルの実装とテスト、動作検証。'
argument-hint: '検証対象の章番号またはドキュメントファイルパスを指定'
---

# ASP.NET Core ハンドブック コードサンプル検証スキル

## 概要

このスキルは、`docs/` フォルダ内のマークダウンドキュメントに含まれるコードサンプルを実際に実装し、テストで動作を検証するためのワークフローです。

## 前提条件

- 対象バージョン: `.github/instructions/versioning.instructions.md` のバージョン決定ルールに従い、使用する .NET および ASP.NET Core のバージョンを確定する。プロジェクト作成時の `--framework` オプションや NuGet パッケージのバージョン指定もこのルールに準拠すること。
- 実行環境: 通常の bash ターミナルで `dotnet` コマンドを実行
- `dotnet` コマンド実行時はサンドボックスを使用しない（unsandboxed 実行）。
- テストフレームワーク: xUnit
- アサーションライブラリ: xUnit 組み込み（必要に応じて FluentAssertions を追加可）
- モックライブラリ: NSubstitute（必要な場合のみ）

## ディレクトリ構成

```
src/
└── {NN}/                          # 章番号（2桁ゼロ埋め）
    ├── {SampleName}/              # コードサンプル名（PascalCase）
    │   ├── {SampleName}.csproj    # 実装プロジェクト
    │   └── *.cs                   # 実装ファイル
    └── {SampleName}.Tests/        # テストプロジェクト
        ├── {SampleName}.Tests.csproj
        └── *Tests.cs              # テストファイル
```

### 命名規則

| 要素 | 規則 | 例 |
| --- | --- | --- |
| 章フォルダ | `src/{NN}/`（NN は章番号の2桁ゼロ埋め） | `src/06/` |
| 実装プロジェクト名 | ドキュメントのセクション内容を反映した PascalCase | `FactoryRegistration` |
| テストプロジェクト名 | 実装プロジェクト名 + `.Tests` | `FactoryRegistration.Tests` |
| テストクラス名 | テスト対象クラス名 + `Tests` | `TimeServiceTests` |
| テストメソッド名 | `{テスト対象メソッド}_{条件}_{期待結果}` パターン | `GetTime_WhenCalled_ReturnsCurrentTime` |

## 手順

### 1. コードサンプルの抽出

対象ドキュメントからコードブロック（` ```csharp ` で囲まれた部分）を特定する。

- 各コードサンプルに対して、そのサンプルが示す概念・機能を把握する
- 断片的なコードサンプル（1〜2行の説明用スニペット）はテスト対象外とする
- 完全なクラス定義、メソッド定義、または `Program.cs` の設定コードをテスト対象とする

### 2. プロジェクトの作成

コードサンプル1つにつき、実装プロジェクト1つとテストプロジェクト1つを作成する。

#### 実装プロジェクトの作成

```bash
cd /workspaces/aspnetcore-ja-handbook
mkdir -p src/{NN}/{SampleName}
cd src/{NN}/{SampleName}
dotnet new classlib -n {SampleName} --framework net{N}.0
```

> [!NOTE]
> コードサンプルが ASP.NET Core の WebApplication を必要とする場合は `classlib` の代わりに `web` または `webapi` テンプレートを使用する。

#### テストプロジェクトの作成

```bash
cd /workspaces/aspnetcore-ja-handbook/src/{NN}
dotnet new xunit -n {SampleName}.Tests --framework net{N}.0
cd {SampleName}.Tests
dotnet add reference ../{SampleName}/{SampleName}.csproj
```

#### 必要なパッケージの追加

コードサンプルの内容に応じて、以下のパッケージを追加する。

```bash
# DI コンテナーが必要な場合
dotnet add package Microsoft.Extensions.DependencyInjection

# ホスティングが必要な場合
dotnet add package Microsoft.Extensions.Hosting

# ASP.NET Core のテストが必要な場合（テストプロジェクトに追加）
dotnet add package Microsoft.AspNetCore.Mvc.Testing

# モックが必要な場合（テストプロジェクトに追加）
dotnet add package NSubstitute
```

### 3. 実装の作成

- ドキュメントのコードサンプルを **そのまま** 実装ファイルに配置する
- コードサンプルが断片的な場合は、コンパイルが通るよう最小限の補完を行う
- 補完した部分にはコメントで `// ドキュメント外: テスト用に追加` と注記する
- `namespace` はプロジェクト名と一致させる

### 4. テストの作成

テストは以下の方針で作成する。

#### テストの観点

1. **コンパイル確認** — コードサンプルがコンパイルエラーなくビルドできること
2. **動作確認** — コードサンプルの説明通りの動作をすること
3. **DI 登録確認** — サービスが正しく解決できること（DI 関連の場合）

#### テストの書き方

```csharp
using Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace SampleName.Tests;

public class SampleClassTests
{
    [Fact]
    public void Method_Condition_ExpectedResult()
    {
        // Arrange
        var services = new ServiceCollection();
        // ドキュメントのコードサンプル通りにサービスを登録

        // Act
        var provider = services.BuildServiceProvider();
        var result = provider.GetRequiredService<ITargetService>();

        // Assert
        Assert.NotNull(result);
    }
}
```

### 5. ビルドとテストの実行

```bash
cd /workspaces/aspnetcore-ja-handbook/src/{NN}/{SampleName}.Tests
dotnet build
dotnet test
```

- すべてのテストが ✅ パスすることを確認する
- ビルドエラーやテスト失敗が発生した場合は、ドキュメント側のコードサンプルに誤りがないか確認する
- ドキュメントのコードサンプルに誤りがある場合は、修正内容を報告する

### 6. 結果の報告

検証結果は以下の形式で報告する。

| コードサンプル | 実装パス | テストパス | 結果 | 備考 |
| --- | --- | --- | --- | --- |
| セクション名 - サンプル説明 | `src/NN/SampleName/` | `src/NN/SampleName.Tests/` | ✅ / ❌ | エラー内容等 |

### ドキュメント修正が必要な場合

テストの結果、ドキュメントのコードサンプルに誤りが見つかった場合:

1. 正しいコードを特定する（MS Learn で裏取りを行う）
2. ドキュメント側のコードブロックを修正する
3. テストが通ることを再確認する
4. 修正内容を結果報告に含める

## ソリューションファイル

章ごとにソリューションファイルを作成し、その章のすべてのプロジェクトをまとめる。

```bash
cd /workspaces/aspnetcore-ja-handbook/src/{NN}
dotnet new sln -n Chapter{NN}
dotnet sln add {SampleName}/{SampleName}.csproj
dotnet sln add {SampleName}.Tests/{SampleName}.Tests.csproj
```

既にソリューションファイルが存在する場合は、新しいプロジェクトを追加するだけでよい。

## 注意事項

- テストプロジェクトでは `IServiceProvider` を直接使ったテストを許容する（テスト対象が DI の動作確認であるため）
- `WebApplicationFactory<T>` を使う統合テストは、コントローラーやエンドポイントのテストでのみ使用する
- テスト内で外部リソース（DB、ネットワーク）に依存しないこと。必要な場合はモックを使用する
- `ImplicitUsings` と `Nullable` は有効のままにする（.NET 10 のデフォルト設定）
