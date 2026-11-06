using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace WinTips.Core;

/// <summary>界面语言：跟随系统 / 简体中文 / 繁體中文 / English / Deutsch / 日本語 / Русский / Español / Português。</summary>
public enum AppLanguage
{
    System,
    ChineseSimplified,
    ChineseTraditional,
    English,
    German,
    Japanese,
    Russian,
    Spanish,
    Portuguese,
}

/// <summary>下拉框里的一个语言选项：显示名一律用该语言自己的写法（与 Windows 语言设置一致）。</summary>
public sealed record LanguageOption(AppLanguage Language, string NativeName)
{
    /// <summary>下拉框与选中项都直接取这个字符串，模板里不必再配 DisplayMemberPath。</summary>
    public override string ToString() => NativeName;
}

/// <summary>
/// 运行时本地化：一张多语言对照词条表 + 语言切换通知。
/// XAML 用 <c>Text="{loc:Tr SettingsTitle}"</c>（见 <see cref="TrExtension"/>）绑定到索引器，
/// 切换语言时以 "Item[]" 通知让所有绑定在同一帧刷新，无需重建窗口；
/// 代码里用 <see cref="T"/> / <see cref="Format"/> 取词。
/// 某语言缺词条时回落英文，再缺则原样返回键名（便于发现漏翻）。
/// </summary>
public static class Loc
{
    /// <summary>词条行内各语言的固定下标，与 <see cref="IndexOf"/> 一一对应。</summary>
    private const int ZhHans = 0, ZhHant = 1, En = 2, De = 3, Ja = 4, Ru = 5, Es = 6, Pt = 7;
    private const int ColumnCount = 8;

    private static readonly Dictionary<string, string[]> Table = new(StringComparer.Ordinal);

    static Loc()
    {
        // —— 设置窗口 ——
        Add("SettingsTitle",
            "WinTips 设置", "WinTips 設定", "WinTips Settings",
            "WinTips-Einstellungen", "WinTips の設定", "Настройки WinTips",
            "Configuración de WinTips", "Configurações do WinTips");
        Add("SettingsHeader",
            "键盘状态提示", "鍵盤狀態提示", "Keyboard status tips",
            "Tastaturstatus-Hinweise", "キーボード状態の通知", "Индикаторы состояния клавиатуры",
            "Avisos de estado del teclado", "Avisos de status do teclado");
        Add("SettingsSubtitle",
            "按下对应按键时，在屏幕顶部或任务栏上方显示系统风格提示",
            "按下對應按鍵時，在畫面頂端或工作列上方顯示系統樣式提示",
            "A system-style tip at the top or above the taskbar on each key press",
            "Ein Hinweis im Systemstil oben oder über der Taskleiste bei jedem Tastendruck",
            "キーを押すと、画面上部またはタスクバーの上にシステム風の通知を表示します",
            "Системное уведомление сверху или над панелью задач при каждом нажатии клавиши",
            "Un aviso al estilo del sistema arriba o sobre la barra de tareas al pulsar una tecla",
            "Um aviso no estilo do sistema no topo ou acima da barra de tarefas a cada tecla");

        Add("SectionKeys",
            "提示浮窗", "提示浮動視窗", "Flyout",
            "Einblendung", "ポップアップ", "Всплывающее окно",
            "Ventana emergente", "Janela flutuante");
        Add("KeyCapsLockLong",
            "大写锁定（Caps Lock）", "大寫鎖定（Caps Lock）", "Caps Lock",
            "Feststelltaste (Caps Lock)", "Caps Lock（大文字ロック）", "Caps Lock (фиксация регистра)",
            "Bloq Mayús (Caps Lock)", "Caps Lock (fixação de maiúsculas)");
        Add("KeyNumLockLong",
            "数字锁定（Num Lock）", "數字鎖定（Num Lock）", "Num Lock",
            "Num-Taste (Num Lock)", "Num Lock（テンキー）", "Num Lock (цифровая клавиатура)",
            "Bloq Num (Num Lock)", "Num Lock (teclado numérico)");
        Add("KeyScrollLockLong",
            "滚动锁定（Scroll Lock）", "捲動鎖定（Scroll Lock）", "Scroll Lock",
            "Rollen (Scroll Lock)", "Scroll Lock（スクロールロック）", "Scroll Lock (блокировка прокрутки)",
            "Bloq Despl (Scroll Lock)", "Scroll Lock (rolagem)");
        Add("RowInsert",
            "Insert 键（事件提示，无开/关状态）", "Insert 鍵（事件提示，無開/關狀態）", "Insert key (event only)",
            "Einfg-Taste (nur Ereignis)", "Insert キー（イベントのみ）", "Клавиша Insert (только событие)",
            "Tecla Insert (solo evento)", "Tecla Insert (apenas evento)");
        Add("RowCopy",
            "复制提示（Ctrl+C）", "複製提示（Ctrl+C）", "Copy tip (Ctrl+C)",
            "Kopier-Hinweis (Strg+C)", "コピー通知（Ctrl+C）", "Уведомление о копировании (Ctrl+C)",
            "Aviso de copiar (Ctrl+C)", "Aviso de cópia (Ctrl+C)");
        Add("RowPaste",
            "粘贴提示（Ctrl+V）", "貼上提示（Ctrl+V）", "Paste tip (Ctrl+V)",
            "Einfüge-Hinweis (Strg+V)", "貼り付け通知（Ctrl+V）", "Уведомление о вставке (Ctrl+V)",
            "Aviso de pegar (Ctrl+V)", "Aviso de colagem (Ctrl+V)");
        Add("RowCut",
            "剪切提示（Ctrl+X）", "剪下提示（Ctrl+X）", "Cut tip (Ctrl+X)",
            "Ausschneide-Hinweis (Strg+X)", "切り取り通知（Ctrl+X）", "Уведомление о вырезании (Ctrl+X)",
            "Aviso de cortar (Ctrl+X)", "Aviso de recorte (Ctrl+X)");
        Add("RowAnyCopy",
            "任意复制方式都提示", "任何複製方式都提示", "Tip for any copy method",
            "Hinweis bei jeder Kopiermethode", "あらゆるコピー方法で通知", "Уведомлять при любом способе копирования",
            "Aviso para cualquier método de copia", "Aviso para qualquer método de cópia");
        Add("RowAnyCopyDesc",
            "右键菜单、应用内“复制”按钮等非键盘复制也弹出提示（需开启复制提示）",
            "右鍵功能表、應用程式內的「複製」按鈕等非鍵盤複製也會提示（需開啟複製提示）",
            "Also tip for context menus and in-app Copy buttons (requires the Copy tip)",
            "Auch bei Kontextmenüs und Kopier-Schaltflächen in Apps (erfordert den Kopier-Hinweis)",
            "右クリックメニューやアプリ内の「コピー」ボタンでも通知します（コピー通知がオンの場合）",
            "Также уведомлять при копировании через контекстное меню и кнопки в приложениях (нужно включить уведомление о копировании)",
            "También avisa al usar menús contextuales o botones Copiar de las aplicaciones (requiere el aviso de copiar)",
            "Também avisa em menus de contexto e botões Copiar dos aplicativos (requer o aviso de cópia)");

        Add("SectionGeneral",
            "通用", "一般", "General",
            "Allgemein", "全般", "Общие",
            "General", "Geral");
        Add("RowDuration",
            "提示停留时长", "提示停留時間", "Display duration",
            "Anzeigedauer", "表示時間", "Длительность показа",
            "Duración del aviso", "Duração da exibição");
        Add("RowAnimSpeed",
            "浮现动画速度", "浮現動畫速度", "Animation speed",
            "Animationsgeschwindigkeit", "アニメーション速度", "Скорость анимации",
            "Velocidad de animación", "Velocidade da animação");
        Add("RowTipScale",
            "浮窗大小", "浮動視窗大小", "Tip size",
            "Größe des Hinweises", "通知のサイズ", "Размер уведомления",
            "Tamaño del aviso", "Tamanho do aviso");
        Add("RowTipScaleDesc",
            "拖动时浮窗实时预览；文字、图标与指示条一起等比缩放",
            "拖動時浮動視窗即時預覽；文字、圖示與指示列一起等比縮放",
            "Live preview while dragging; text, icon and indicator scale together",
            "Live-Vorschau beim Ziehen; Text, Symbol und Balken skalieren gemeinsam",
            "ドラッグ中はリアルタイムでプレビュー。文字・アイコン・インジケーターがまとめて拡大縮小します",
            "Предпросмотр при перетаскивании; текст, значок и индикатор масштабируются вместе",
            "Vista previa en vivo al arrastrar; el texto, el icono y la barra se escalan juntos",
            "Pré-visualização ao arrastar; texto, ícone e indicador são dimensionados juntos");
        Add("BtnResetSize",
            "还原默认", "還原預設值", "Reset",
            "Zurücksetzen", "既定値に戻す", "Сбросить",
            "Restablecer", "Redefinir");
        Add("RowGlass",
            "玻璃材质", "玻璃材質", "Glass material",
            "Glasmaterial", "ガラス素材", "Стеклянный материал",
            "Material de cristal", "Material de vidro");
        Add("RowGlassDesc",
            "抓取浮窗背后的画面实时模糊，观感接近 Win11 任务栏；关闭则只用系统亚克力",
            "擷取浮動視窗背後的畫面即時模糊，觀感接近 Win11 工作列；關閉則只用系統壓克力",
            "Blurs what is actually behind the flyout, like the Windows 11 taskbar; off keeps the system acrylic only",
            "Zeichnet den echten Hintergrund unscharf, ähnlich der Windows 11-Taskleiste; aus = nur System-Acryl",
            "背後の画面をリアルタイムでぼかし、Windows 11 のタスクバーに近い見た目にします（オフ時はシステムのアクリルのみ）",
            "Размывает реальный фон за уведомлением, как панель задач Windows 11; выкл. — только системный акрил",
            "Desenfoca lo que hay realmente detrás, como la barra de tareas de Windows 11; desactivado solo usa el acrílico del sistema",
            "Desfoca o que está realmente atrás, como a barra de tarefas do Windows 11; desligado usa apenas o acrílico do sistema");
        Add("RowGlassOpacity",
            "透明度", "透明度", "Opacity",
            "Deckkraft", "不透明度", "Непрозрачность",
            "Opacidad", "Opacidade");
        Add("RowPosition",
            "提示位置", "提示位置", "Tip position",
            "Position des Hinweises", "通知の位置", "Положение уведомления",
            "Posición del aviso", "Posição do aviso");
        Add("PositionTop",
            "顶部", "頂端", "Top",
            "Oben", "上", "Сверху",
            "Arriba", "Superior");
        Add("PositionBottom",
            "底部", "底部", "Bottom",
            "Unten", "下", "Снизу",
            "Abajo", "Inferior");
        Add("PositionLeft",
            "左侧", "左側", "Left",
            "Links", "左", "Слева",
            "Izquierda", "Esquerda");
        Add("PositionCenter",
            "居中", "置中", "Center",
            "Mitte", "中央", "По центру",
            "Centro", "Centro");
        Add("PositionRight",
            "右侧", "右側", "Right",
            "Rechts", "右", "Справа",
            "Derecha", "Direita");
        Add("RowCustomPos",
            "自定义位置", "自訂位置", "Custom position",
            "Eigene Position", "カスタム位置", "Своё положение",
            "Posición personalizada", "Posição personalizada");
        Add("RowCustomPosDesc",
            "把提示固定到屏幕任意位置（相对所在显示器的左上角）",
            "將提示固定在畫面任意位置（相對於所在螢幕的左上角）",
            "Pin the tip anywhere on screen (relative to the top-left of its display)",
            "Hinweis an einer beliebigen Stelle fixieren (relativ zur linken oberen Ecke des Bildschirms)",
            "通知を画面上の任意の位置に固定します（表示先の画面左上が基準）",
            "Закрепить уведомление в любом месте экрана (относительно левого верхнего угла монитора)",
            "Fija el aviso en cualquier punto de la pantalla (respecto a la esquina superior izquierda de su monitor)",
            "Fixa o aviso em qualquer ponto da tela (em relação ao canto superior esquerdo do monitor)");
        Add("BtnAdjustPos",
            "调整位置", "調整位置", "Adjust",
            "Anpassen", "位置を調整", "Настроить",
            "Ajustar", "Ajustar");
        Add("BtnResetPos",
            "还原默认", "還原預設值", "Reset",
            "Zurücksetzen", "既定値に戻す", "Сбросить",
            "Restablecer", "Redefinir");
        Add("RowFocusMonitor",
            "跟随焦点显示器", "跟隨焦點螢幕", "Follow the focused display",
            "Aktivem Bildschirm folgen", "フォーカス中の画面に合わせる", "Следовать за активным монитором",
            "Seguir al monitor activo", "Seguir o monitor em foco");
        Add("RowFocusMonitorDesc",
            "提示出现在当前活动窗口所在的显示器，而不是固定主显示器",
            "提示會出現在目前作用中視窗所在的螢幕，而不是固定主螢幕",
            "Show the tip on the display with the active window instead of always the primary one",
            "Hinweis auf dem Bildschirm des aktiven Fensters anzeigen statt immer auf dem Hauptbildschirm",
            "通知を常にメイン画面ではなく、アクティブなウィンドウがある画面に表示します",
            "Показывать уведомление на мониторе с активным окном, а не всегда на основном",
            "Muestra el aviso en el monitor de la ventana activa en lugar de siempre en el principal",
            "Mostra o aviso no monitor da janela ativa em vez de sempre no principal");
        Add("RowStartup",
            "开机自动运行", "開機時自動啟動", "Run at startup",
            "Beim Start ausführen", "スタートアップで実行", "Запускать при входе",
            "Ejecutar al iniciar", "Executar na inicialização");
        Add("StartupHintBlockedByUser",
            "Windows 记住了你之前在任务管理器里的关闭操作，只能由你手动重新打开：设置 > 应用 > 启动。",
            "Windows 記住了你先前在工作管理員裡的關閉操作，只能由你手動重新開啟：設定 > 應用程式 > 啟動。",
            "Windows remembers that you turned this off in Task Manager. Only you can turn it back on: Settings > Apps > Startup.",
            "Windows merkt sich, dass Sie dies im Task-Manager deaktiviert haben. Nur Sie können es wieder aktivieren: Einstellungen > Apps > Autostart.",
            "Windows はタスク マネージャーでの無効化を記憶しています。手動で再度有効にする必要があります: 設定 > アプリ > スタートアップ。",
            "Windows помнит, что вы отключили это в Диспетчере задач. Включить снова можете только вы: Параметры > Приложения > Автозагрузка.",
            "Windows recuerda que lo desactivaste en el Administrador de tareas. Solo tú puedes volver a activarlo: Configuración > Aplicaciones > Inicio.",
            "O Windows lembra que você desativou isso no Gerenciador de Tarefas. Só você pode reativar: Configurações > Aplicativos > Inicialização.");
        Add("StartupHintBlockedByPolicy",
            "此设备的组策略禁止应用自行开启开机自启。",
            "此裝置的群組原則禁止應用程式自行開啟開機自動啟動。",
            "Group policy on this device blocks apps from enabling startup.",
            "Die Gruppenrichtlinie dieses Geräts verhindert, dass Apps den Autostart aktivieren.",
            "このデバイスのグループ ポリシーにより、アプリからのスタートアップ登録は禁止されています。",
            "Групповая политика на этом устройстве запрещает приложениям включать автозапуск.",
            "La directiva de grupo de este dispositivo impide que las aplicaciones activen el inicio automático.",
            "A política de grupo deste dispositivo impede que os aplicativos ativem a inicialização automática.");
        Add("StartupHintFailed",
            "无法修改开机自启状态，请稍后重试。",
            "無法變更開機自動啟動狀態，請稍後再試。",
            "Could not change the startup setting. Please try again.",
            "Die Autostart-Einstellung konnte nicht geändert werden. Bitte erneut versuchen.",
            "スタートアップの設定を変更できませんでした。後でもう一度お試しください。",
            "Не удалось изменить автозапуск. Повторите попытку позже.",
            "No se pudo cambiar el inicio automático. Inténtalo de nuevo más tarde.",
            "Não foi possível alterar a inicialização automática. Tente novamente mais tarde.");

        Add("RowLanguage",
            "语言", "語言", "Language",
            "Sprache", "言語", "Язык",
            "Idioma", "Idioma");
        // 开关标签，尽量短：它和语言下拉框同处一行
        Add("LangSystem",
            "跟随系统", "跟隨系統", "Follow system",
            "Systemsprache", "システムに合わせる", "Как в системе",
            "Seguir al sistema", "Seguir o sistema");

        // —— 数值标签 ——
        Add("ValueSeconds",
            "{0} 秒", "{0} 秒", "{0} s",
            "{0} s", "{0} 秒", "{0} с",
            "{0} s", "{0} s");
        Add("ValueAnimSpeed",
            "{0}（{1} 毫秒）", "{0}（{1} 毫秒）", "{0} ({1} ms)",
            "{0} ({1} ms)", "{0}（{1} ミリ秒）", "{0} ({1} мс)",
            "{0} ({1} ms)", "{0} ({1} ms)");
        Add("AnimNone",
            "无动画", "無動畫", "No animation",
            "Keine Animation", "アニメーションなし", "Без анимации",
            "Sin animación", "Sem animação");

        // —— 浮窗文案 ——
        Add("KeyCapsLock",
            "大写锁定", "大寫鎖定", "Caps Lock",
            "Feststelltaste", "Caps Lock", "Caps Lock",
            "Bloq Mayús", "Caps Lock");
        Add("KeyNumLock",
            "数字锁定", "數字鎖定", "Num Lock",
            "Num-Taste", "Num Lock", "Num Lock",
            "Bloq Num", "Num Lock");
        Add("KeyScrollLock",
            "滚动锁定", "捲動鎖定", "Scroll Lock",
            "Rollen", "Scroll Lock", "Scroll Lock",
            "Bloq Despl", "Scroll Lock");
        Add("StateOn",
            "开启", "開啟", "On",
            "An", "オン", "Вкл",
            "Activado", "Ativado");
        Add("StateOff",
            "关闭", "關閉", "Off",
            "Aus", "オフ", "Выкл",
            "Desactivado", "Desativado");
        Add("TipToggleFormat",
            "{0} {1}", "{0} {1}", "{0} {1}",
            "{0} {1}", "{0} {1}", "{0} {1}",
            "{0} {1}", "{0} {1}");
        Add("TipInsertPressed",
            "Insert 键已按下", "Insert 鍵已按下", "Insert pressed",
            "Einfg gedrückt", "Insert キーを押しました", "Insert нажата",
            "Insert pulsado", "Insert pressionado");
        Add("TipCopied",
            "已复制", "已複製", "Copied",
            "Kopiert", "コピーしました", "Скопировано",
            "Copiado", "Copiado");
        Add("TipPasted",
            "已粘贴", "已貼上", "Pasted",
            "Eingefügt", "貼り付けました", "Вставлено",
            "Pegado", "Colado");
        Add("TipCut",
            "已剪切", "已剪下", "Cut",
            "Ausgeschnitten", "切り取りました", "Вырезано",
            "Cortado", "Recortado");
        Add("SampleTipOn",
            "大写锁定 开启", "大寫鎖定 開啟", "Caps Lock On",
            "Feststelltaste An", "Caps Lock オン", "Caps Lock Вкл",
            "Bloq Mayús Activado", "Caps Lock Ativado");

        // —— 托盘菜单 ——
        Add("MenuSettings",
            "设置", "設定", "Settings",
            "Einstellungen", "設定", "Параметры",
            "Configuración", "Configurações");
        Add("MenuTest",
            "测试提示", "測試提示", "Test tips",
            "Hinweise testen", "通知をテスト", "Проверить уведомления",
            "Probar avisos", "Testar avisos");
        Add("MenuExit",
            "退出", "結束", "Exit",
            "Beenden", "終了", "Выход",
            "Salir", "Sair");
        // 托盘悬停提示不再走本地化：只显示「WinTips V<版本号>」（版本见 csproj）

        // —— 自定义位置模式 ——
        Add("PickTitle",
            "自定义提示位置", "自訂提示位置", "Custom tip position",
            "Eigene Position", "通知のカスタム位置", "Своё положение уведомления",
            "Posición personalizada del aviso", "Posição personalizada do aviso");
        Add("PickHint",
            "按住浮窗拖动，或用方向键逐像素微调（Shift 每次 10 像素）",
            "按住浮動視窗拖曳，或用方向鍵逐像素微調（Shift 每次 10 像素）",
            "Drag the tip, or nudge it with the arrow keys (Shift = 10 px)",
            "Hinweis ziehen oder mit den Pfeiltasten verschieben (Umschalt = 10 px)",
            "通知をドラッグするか、矢印キーで微調整します（Shift で 10 ピクセル）",
            "Перетащите уведомление или сдвиньте стрелками (Shift — по 10 пикселей)",
            "Arrastra el aviso o ajústalo con las flechas (Mayús = 10 px)",
            "Arraste o aviso ou ajuste com as setas (Shift = 10 px)");
        Add("PickZoneHint",
            "虚线内为贴边弹出区（左右优先于上下），靠近中轴自动吸附对齐",
            "虛線內為貼邊彈出區（左右優先於上下），靠近中軸會自動對齊",
            "Dashed bands set the slide-in edge (Left/Right outrank Top/Bottom); snaps to the center axes",
            "Gestrichelte Bereiche legen die Einblendkante fest (links/rechts vor oben/unten); rastet an den Mittelachsen ein",
            "点線の範囲がスライドインする辺です（左右が上下より優先）。中央軸に吸着します",
            "Пунктирные зоны задают сторону появления (лево/право важнее верха/низа); притягивается к центральным осям",
            "Las franjas discontinuas definen el borde de entrada (izquierda/derecha tienen prioridad); se ajusta a los ejes centrales",
            "As faixas tracejadas definem a borda de entrada (esquerda/direita têm prioridade); encaixa nos eixos centrais");
        Add("PickCancel",
            "取消", "取消", "Cancel",
            "Abbrechen", "キャンセル", "Отмена",
            "Cancelar", "Cancelar");
        Add("PickSave",
            "保存位置", "儲存位置", "Save position",
            "Position speichern", "位置を保存", "Сохранить положение",
            "Guardar posición", "Salvar posição");

        // —— 单实例提示窗 ——
        Add("NoticeRunningTitle",
            "WinTips 已在运行", "WinTips 已在執行", "WinTips is already running",
            "WinTips läuft bereits", "WinTips は既に実行中です", "WinTips уже запущен",
            "WinTips ya se está ejecutando", "O WinTips já está em execução");
        Add("NoticeRunningMessage",
            "另一个 WinTips 实例已经在运行", "另一個 WinTips 執行個體已經在執行", "Another instance of WinTips is already running",
            "Eine weitere Instanz von WinTips läuft bereits", "別の WinTips インスタンスが既に実行中です", "Другой экземпляр WinTips уже запущен",
            "Ya hay otra instancia de WinTips en ejecución", "Outra instância do WinTips já está em execução");
        Add("NoticeOk",
            "确定", "確定", "OK",
            "OK", "OK", "ОК",
            "Aceptar", "OK");
    }

    /// <summary>登记一条词条：参数顺序固定为 简中、繁中、英、德、日、俄、西、葡。</summary>
    private static void Add(
        string key,
        string zhHans, string zhHant, string en,
        string de, string ja, string ru, string es, string pt)
    {
        var row = new string[ColumnCount];
        row[ZhHans] = zhHans;
        row[ZhHant] = zhHant;
        row[En] = en;
        row[De] = de;
        row[Ja] = ja;
        row[Ru] = ru;
        row[Es] = es;
        row[Pt] = pt;
        Table[key] = row;
    }

    /// <summary>下拉框选项：语言名称用它自己的写法，切语言时不需要重排。</summary>
    public static IReadOnlyList<LanguageOption> Options { get; } = new[]
    {
        new LanguageOption(AppLanguage.ChineseSimplified, "简体中文"),
        new LanguageOption(AppLanguage.ChineseTraditional, "繁體中文"),
        new LanguageOption(AppLanguage.English, "English"),
        new LanguageOption(AppLanguage.German, "Deutsch"),
        new LanguageOption(AppLanguage.Japanese, "日本語"),
        new LanguageOption(AppLanguage.Russian, "Русский"),
        new LanguageOption(AppLanguage.Spanish, "Español"),
        new LanguageOption(AppLanguage.Portuguese, "Português"),
    };

    /// <summary>当前语言设置（可能是 System）。</summary>
    public static AppLanguage Language { get; private set; } = AppLanguage.System;

    /// <summary>实际生效的语言（System 时已按系统 UI 语言解析成具体一种）。</summary>
    public static AppLanguage Effective { get; private set; } = AppLanguage.ChineseSimplified;

    /// <summary>语言切换后触发（用于刷新代码里临时拼好的文案，如滑杆数值标签）。</summary>
    public static event Action? Changed;

    /// <summary>应用语言。必须在创建任何窗口之前调用一次。</summary>
    public static void Apply(AppLanguage language)
    {
        Language = language;
        Effective = language == AppLanguage.System ? DetectSystemLanguage() : language;

        TrSource.Instance.Refresh();
        Changed?.Invoke();
    }

    /// <summary>按系统 UI 语言挑最接近的一种；没有对应语言时回落英文。</summary>
    private static AppLanguage DetectSystemLanguage()
    {
        var culture = CultureInfo.CurrentUICulture;
        string name = culture.Name;
        string two = culture.TwoLetterISOLanguageName.ToLowerInvariant();

        if (two == "zh")
            return Contains(name, "Hant") || Contains(name, "TW") || Contains(name, "HK") || Contains(name, "MO")
                ? AppLanguage.ChineseTraditional
                : AppLanguage.ChineseSimplified;

        return two switch
        {
            "de" => AppLanguage.German,
            "ja" => AppLanguage.Japanese,
            "ru" => AppLanguage.Russian,
            "es" => AppLanguage.Spanish,
            "pt" => AppLanguage.Portuguese,
            _ => AppLanguage.English,
        };
    }

    private static bool Contains(string name, string value) =>
        name.Contains(value, StringComparison.OrdinalIgnoreCase);

    /// <summary>设置值 → 枚举（非法值回落为跟随系统）。</summary>
    public static AppLanguage Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "zh" or "cn" or "zh-cn" or "zh-hans" or "chs" or "chinese" or "chinese-simplified" => AppLanguage.ChineseSimplified,
        "zh-tw" or "zh-hk" or "zh-mo" or "zh-hant" or "cht" or "traditional" or "chinese-traditional" => AppLanguage.ChineseTraditional,
        "en" or "en-us" or "en-gb" or "english" => AppLanguage.English,
        "de" or "de-de" or "german" or "deutsch" => AppLanguage.German,
        "ja" or "jp" or "ja-jp" or "japanese" => AppLanguage.Japanese,
        "ru" or "ru-ru" or "russian" => AppLanguage.Russian,
        "es" or "es-es" or "spanish" or "espanol" or "español" => AppLanguage.Spanish,
        "pt" or "pt-br" or "pt-pt" or "portuguese" or "portugues" or "português" => AppLanguage.Portuguese,
        _ => AppLanguage.System,
    };

    /// <summary>枚举 → 设置值。</summary>
    public static string ToSetting(AppLanguage language) => language switch
    {
        AppLanguage.ChineseSimplified => "zh",
        AppLanguage.ChineseTraditional => "zh-Hant",
        AppLanguage.English => "en",
        AppLanguage.German => "de",
        AppLanguage.Japanese => "ja",
        AppLanguage.Russian => "ru",
        AppLanguage.Spanish => "es",
        AppLanguage.Portuguese => "pt",
        _ => "System",
    };

    /// <summary>语言 → 词条行下标。</summary>
    private static int IndexOf(AppLanguage language) => language switch
    {
        AppLanguage.ChineseSimplified or AppLanguage.System => ZhHans,
        AppLanguage.ChineseTraditional => ZhHant,
        AppLanguage.English => En,
        AppLanguage.German => De,
        AppLanguage.Japanese => Ja,
        AppLanguage.Russian => Ru,
        AppLanguage.Spanish => Es,
        AppLanguage.Portuguese => Pt,
        _ => En,
    };

    /// <summary>取词条：当前语言缺词条时回落英文，仍缺则原样返回键名。</summary>
    public static string T(string key)
    {
        if (!Table.TryGetValue(key, out var row)) return key;

        string text = row[IndexOf(Effective)];
        if (!string.IsNullOrEmpty(text)) return text;

        text = row[En];
        return string.IsNullOrEmpty(text) ? key : text;
    }

    /// <summary>取带占位符的词条并格式化。</summary>
    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);
}

/// <summary>
/// XAML 绑定的数据源：索引器承担取词，切换语言时以 "Item[]" 通知刷新全部索引器绑定。
/// </summary>
public sealed class TrSource : INotifyPropertyChanged
{
    public static TrSource Instance { get; } = new();

    private TrSource() { }

    public string this[string key] => Loc.T(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Refresh() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
}

/// <summary>
/// XAML 标记扩展：<c>Text="{loc:Tr SettingsTitle}"</c>。
/// 返回绑定而非静态字符串，语言切换时文案随之更新，窗口无需重建。
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key)) return string.Empty;

        return new Binding($"[{Key}]")
        {
            Source = TrSource.Instance,
            Mode = BindingMode.OneWay,
            FallbackValue = Key,
        }.ProvideValue(serviceProvider);
    }
}
