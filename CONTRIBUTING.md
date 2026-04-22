# 本コンテンツへの貢献方法

`ASP.NET Core ハンドブック` への貢献にご関心をお寄せいただきありがとうございます！誤字の修正、新しいコンテンツの追加、既存ページの改善など、どのような形であれ皆さまの貢献を歓迎いたします。このガイドを参考に、ぜひ貢献を始めてみてください。

## 🤔 貢献の種類

本コンテンツへの貢献には、いくつかの種類があります。

- **軽微な修正** - 誤字・脱字、文法の誤り、書式の問題を修正します。
- **コンテンツの追加** - 不足しているトピックをカバーする新しいドキュメントページやセクションを追加します。
- **コンテンツの改善** - 既存のドキュメントをより分かりやすい説明、最新の情報、追加の例などで改善します。
- **コードへの貢献** - サイトのコードベースの改善、バグ修正、新機能の追加を行います。

貢献の手段は GitHub 上での Issue または Discussion を立てて、その上で Pull Request を作成する形になります。以下のセクションで詳しく説明します。

## ➡️ Git ワークフロー

コンテンツの追加・変更は必ず次の Git ワークフローに従って行ってください。

1. Issue から始める（またはディスカッションを経て Issue を作成する）
2. リポジトリをフォークする

    `ASP.NET Core ハンドブック` リポジトリを自分の GitHub アカウントにフォークします。
 
3. 変更用の新しいブランチを作成する

    ```bash
    git checkout -b feature/your-feature-name
    ```
    ブランチ名は**必ず feature/ で始めてください**。

4. ライティングスタイルガイドを考慮しながら変更を行う
5. 分かりやすいコミットメッセージでコミットする
6. フォーク先にプッシュする
7. Pull Request を作成する
   - Pull Request 作成時の説明は必ずキーワードを使用して Issue のリンクを含める必要があります。例: `fixes #123`、`closes #123` など。詳しくは [GitHub のドキュメント](https://docs.github.com/ja/issues/tracking-your-work-with-issues/using-issues/linking-a-pull-request-to-an-issue#linking-a-pull-request-to-an-issue-using-a-keyword)をご確認ください
   - 必ず [行動規範](CODE_OF_CONDUCT.md) に従ってください

## ✍️ ライティングスタイルガイド

`ASP.NET Core ハンドブック` に貢献する際は、一貫性と明確さを確保するために以下のライティングガイドラインに従ってください：

- **明確で簡潔な言葉を使う** - シンプルさを心がけてください。専門用語は必要な場合を除き避け、技術用語は初出時に説明してください。
- **一貫性を保つ** - 用語、書式、構成について既存の規則に従ってください。他のドキュメントページを参考にしてください。
- **画像・Mermaidによる図解をする** - コンテンツをわかりやすくするために画像・Mermaid記法によるフロー・図の掲載を強く推奨します。特に IDE での使い方に言及する場合は画面のキャプチャは必須です。
- **インクルーシブな表現を使う** - すべての読者を尊重するインクルーシブな言葉遣いを使用してください。性別を限定する用語やステレオタイプは避けてください。
- **例を示す** - 具体的な実装を説明するためにコードスニペットや例を含めてください。
- **正しい文法とスペルを使う** - 貢献内容を校正し、誤りや誤字がないことを確認してください。
- **論理的にコンテンツを構成する** - 見出し、小見出し、リストを使って、情報を分かりやすく整理してください。
- **関連リソースへのリンクを貼る** - 概念、ツール、関連ドキュメントについて、読者が詳細情報を見つけられるよう参考ドキュメントセクションをコンテンツの最後に作成し、参照リンクを提供してください。
- **既存コンテンツを確認する** - 新しいコンテンツを追加する前に、既存のドキュメントを確認し、重複を避けて一貫性を保ってください。

## 📝 マークダウンの書き方

本コンテンツは GitHub 上でマークダウン形式で書かれています。マークダウンの書き方については以下の GitHub 公式ドキュメントを参照してください。

- [基本的な書き込みと書式設定の構文](https://docs.github.com/ja/get-started/writing-on-github/getting-started-with-writing-and-formatting-on-github/basic-writing-and-formatting-syntax)

以降に本コンテンツ固有の記載方法について説明します。

### IDEごとのコンテンツの書き方

.NET CLI と Visual Studio、Visual Studio Code + C# Dev Kit で異なる固有のコンテンツを文書化する場合は、`<details>` タグと `<summary>` タグを使用して、セクションを折りたたみ可能にしてください。

例:

````markdown
<details>
<summary>CLI</summary>

ターミナルで以下のコマンドを実行します。

```bash
dotnet add src/Web/Web.csproj reference src/ApplicationCore/ApplicationCore.csproj
```

</details>
````

この実装は次のように表示されます。
<details>
<summary>CLI</summary>

ターミナルで以下のコマンドを実行します。

```bash
dotnet add src/Web/Web.csproj reference src/ApplicationCore/ApplicationCore.csproj
```

</details>

### 他言語・他フレームワークの経験者向けのコンテンツの書き方

コンテンツ内で他言語・他フレームワークでの似たような機能や概念を説明する場合、`[!TIP]` を使用して、以下のように記載してください。

```
> [!TIP]
> NuGet は .NET 標準のパッケージ管理システムであり、Java における Maven/Gradle 、JavaScript における npm 、PHP における Composer に近いものです。
```

> [!TIP]
> NuGet は .NET 標準のパッケージ管理システムであり、Java における Maven/Gradle 、JavaScript における npm 、PHP における Composer に近いものです。
