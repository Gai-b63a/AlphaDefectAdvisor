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

初回は Steam にログインし、検証済みの `dist/workshop` フォルダーを Windows のエクスプローラーから公式の `ModUploader.exe` にドラッグ＆ドロップして公開します。成功時に生成される `mod_id.txt` を、このリポジトリの `workshop/mod_id.txt` として `main` に登録してください。ファイルの中身は公開アイテムの数値 ID だけです。

自動更新には GitHub Environment `steam-workshop` の `STEAM_USERNAME` と `STEAM_PASSWORD` を使います。初回投稿した Steam アカウントを設定し、Environment の対象を `main` に限定してください。Steam が新しい端末の認証を求めた場合は Steam Mobile で承認します。

以後、実機確認した変更とともに `advisor/AlphaDefectAdvisor.json` の `version`（major.minor.patch）を上げて `main` に反映すると、その ID のアイテムが自動更新されます。変更内容は `workshop/workshop.json` の `changeNote` に記載します。ID 登録だけでは投稿されません。再試行が必要な場合は **Actions → Steam Workshop → Run workflow → main → update** を使います。

[公式 ModUploader](https://github.com/megacrit/sts2-mod-uploader/releases)

## ライセンス

自作部分の許諾は LICENSE、AlphaDefect 由来の許諾と適用範囲は配布物の AlphaDefect-LICENSE.txt と AlphaDefect-NOTICE.md に記載しています。ゲーム本体、抽出画像、ゲーム由来の語彙表やカタログは配布物に含めません。Slay the Spire 2 は Mega Crit のゲームです。本 Mod は有志によるものです。
