# AlphaDefect Advisor

Slay the Spire 2 のゲーム画面内に、ソロのアイアンクラッド向けの助言を日本語で表示します。画面左上のパネルには推薦行動と、最大3候補のモデル選択確率が出ます。カードや対象の選択、ターン終了はプレイヤーが操作します。対象は Windows の Steam 版 v0.113.1 です。

## 初回の導入

Steam ワークショップで購読し、ダウンロードが終わったら Steam からゲームを起動します。初回はゲーム内に準備状況が表示されます。Mod は同梱の専用 Python、CPU 推論ライブラリ、公式 weights-v6 の重みを展開し、固定リビジョンの語彙表など5ファイルを GitHub から取得して SHA-256 で照合します。初回準備中はインターネット接続が必要です。完了後はソロのアイアンクラッドでランを開始してください。

推論環境は利用者ごとの `%LOCALAPPDATA%/AlphaDefectAdvisor/runtime` に作成します。モデルの手動取得、Python・Git の事前インストール、セットアップスクリプトや別アプリの起動は不要です。同じ配布版を再起動すると、照合済みのデータを再利用します。

通常の戦闘、マップ、イベント、休憩所、商店、報酬、カード報酬が対象です。敵ターン、カードの追加選択、未対応画面では待機します。詳細な実機受け入れ条件は開発リポジトリの REQUIREMENTS.md に記載しています。

## 開発用ビルド

開発者は .NET 9 SDK、対応ゲーム版、Windows x64 の標準 CPython 3.12 と pip、Visual Studio の x64 再頒布用 CRT、公式 weights-v6 の `ironclad_ema_v24782.pt` を用意し、重みをリポジトリ直下に置きます。ビルド時に CPU 用 wheel をハッシュ付きの一覧から取得します。

```powershell
git clone https://github.com/hmty-CHEN/AlphaDefect-Public.git upstream
git -C upstream checkout --detach 800db3039162647bf0ad13ed379f155cc5806f5f
$game = 'E:\SteamLibrary\steamapps\common\Slay the Spire 2'
.\Build-Advisor.ps1 -GameAssemblyDir "$game\data_sts2_windows_x86_64"
.\Install-Advisor.ps1 -GameDir $game
```

`Build-Advisor.ps1` は `dist/workshop` に投稿用ワークスペースを作ります。ビルド用 Python は `-BuildPython`、取得済みの wheel は `-Wheelhouse`、CRT の場所は `-CrtDir` で指定できます。

## Steam ワークショップへの公開（メンテナー向け）

初回のみ、GitHub の **Settings → Environments** で `steam-workshop` を作成し、公開用 Steam アカウントのログイン名を `STEAM_USERNAME`、パスワードを `STEAM_PASSWORD` として Environment secrets に登録します。対象ゲームの Windows 版を所有し、Workshop の利用条件に同意済みのアカウントを使います。Environment のデプロイ対象は `main` に限定してください。Workflow が `workshop/mod_id.txt` を更新できるリポジトリ権限が必要です。

**Actions → Steam Workshop → Run workflow** を開き、ブランチを `main` にして、初回は `create`、公開済みアイテムの更新は `update` を選びます。`prepare` はランタイムの作成と検証だけを行います。Steam が新しい端末からのログイン確認を求めたら Steam Mobile で承認してください。この経路はモバイル承認または追加入力のないログインに対応します。メール認証コードの対話入力には対応していません。

GitHub の一時 Windows ランナーがランタイムを作成し、Steam から対象バージョンのゲームを取得して Mod をビルド・検証します。実機で受け入れ確認したリビジョンを公開してください。成功すると Actions の実行結果に Steam の公開 URL が表示され、初回のアイテム ID は `workshop/mod_id.txt` に保存されます。

初回投稿の途中で停止し、ID が `pending:...` の場合は、Steam の自分のワークショップ項目と Actions の実行結果を確認してください。作成済みなら GitHub の編集画面で同ファイルを数値 ID に置き換えて `update` を実行します。作成されていないことを確認できた場合だけ同ファイルを削除し、`create` を再実行します。

## ライセンス

自作部分の許諾は LICENSE、AlphaDefect 由来の許諾と適用範囲は配布物の AlphaDefect-LICENSE.txt と AlphaDefect-NOTICE.md に記載しています。ゲーム本体、抽出画像、ゲーム由来の語彙表やカタログは配布物に含めません。Slay the Spire 2 は Mega Crit のゲームです。本 Mod は有志によるものです。
