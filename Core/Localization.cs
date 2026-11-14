using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace WinTips.Core;

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

public sealed record LanguageOption(AppLanguage Language, string NativeName)
{
    public override string ToString() => NativeName;
}

public static class Loc
{
    private const int ZhHans = 0, ZhHant = 1, En = 2, De = 3, Ja = 4, Ru = 5, Es = 6, Pt = 7;
    private const int ColumnCount = 8;

    private static readonly Dictionary<string, string[]> Table = new(StringComparer.Ordinal);

    static Loc()
    {
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
            "复制提示", "複製提示", "Copy tip",
            "Kopier-Hinweis", "コピー通知", "Уведомление о копировании",
            "Aviso de copiar", "Aviso de cópia");
        Add("RowPaste",
            "粘贴提示", "貼上提示", "Paste tip",
            "Einfüge-Hinweis", "貼り付け通知", "Уведомление о вставке",
            "Aviso de pegar", "Aviso de colagem");
        Add("RowCut",
            "剪切提示", "剪下提示", "Cut tip",
            "Ausschneide-Hinweis", "切り取り通知", "Уведомление о вырезании",
            "Aviso de cortar", "Aviso de recorte");
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
        Add("RowLanguageTip",
            "输入法语言与中英文提示", "輸入法語言與中英文提示", "Input language & Chinese/English tip",
            "Hinweis zu Eingabesprache und Chinesisch/Englisch", "入力言語と中英文の通知",
            "Уведомление о языке и режиме ввода",
            "Aviso de idioma de entrada y de chino/inglés", "Aviso de idioma de entrada e de chinês/inglês");
        Add("RowLanguageTipDesc",
            "Shift 切中英文、Win+Space 换输入语言后，浮窗显示当前状态",
            "Shift 切中英文、Win+Space 換輸入語言後，浮動視窗顯示目前狀態",
            "Show the current state after toggling Chinese/English (Shift) or switching language (Win+Space)",
            "Zeigt den aktuellen Zustand nach Umschalten Chinesisch/Englisch (Shift) oder Spracheingabe (Win+Space)",
            "Shift で中英文、Win+Space で入力言語を切り替えると、現在の状態を表示します",
            "Показывать текущее состояние после переключения 中/EN (Shift) или языка ввода (Win+Space)",
            "Muestra el estado actual al alternar chino/inglés (Shift) o cambiar de idioma (Win+Space)",
            "Mostra o estado atual ao alternar chinês/inglês (Shift) ou trocar de idioma (Win+Space)");
        Add("RowLanguageOnSwitch",
            "切换窗口时提示输入状态", "切換視窗時提示輸入狀態", "Tip when switching windows",
            "Hinweis beim Fensterwechsel", "ウィンドウ切り替え時の通知",
            "Уведомление при переключении окон",
            "Aviso al cambiar de ventana", "Aviso ao alternar janelas");
        Add("RowLanguageOnSwitchDesc",
            "开启系统的“允许我为每个应用窗口使用不同输入法”后，切窗口引起的语言或中英文变化也弹出提示（需开启上面的输入法提示）",
            "開啟系統的「允許我為每個應用程式視窗使用不同輸入法」後，切換視窗引起的語言或中英文變化也會提示（需開啟上面的輸入法提示）",
            "With Windows' \"Let me use a different input method for each app window\" on, a language or Chinese/English change caused by switching windows also shows the tip (requires the input language tip)",
            "Bei aktiviertem „Eine andere Eingabemethode für jedes App-Fenster verwenden“ meldet auch ein Sprach- oder Chinesisch/Englisch-Wechsel durch einen Fensterwechsel (erfordert den Eingabesprache-Hinweis)",
            "Windows の「アプリごとに異なる入力方法を使う」が有効な場合、ウィンドウ切り替えによる言語・中英文の変化も通知します（入力言語の通知が必要）",
            "При включённой настройке «Разрешить отдельный метод ввода для каждого окна приложения» уведомлять и о смене языка или режима 中/EN при переключении окон (нужно уведомление о языке ввода)",
            "Con «Permitir usar un método de entrada diferente para cada ventana de aplicación» activado, el cambio de idioma o de chino/inglés al cambiar de ventana también muestra el aviso (requiere el aviso de idioma de entrada)",
            "Com «Permitir usar um método de entrada diferente para cada janela de aplicativo» ativado, a mudança de idioma ou de chinês/inglês ao alternar janelas também mostra o aviso (requer o aviso de idioma de entrada)");

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
            "模糊效果", "模糊效果", "Blur effect",
            "Unschärfeeffekt", "ぼかし効果", "Эффект размытия",
            "Efecto de desenfoque", "Efeito de desfoque");
        Add("RowGlassDesc",
            "实时模糊浮窗背后的画面，观感接近 Win11 任务栏；关闭则只用系统亚克力",
            "即時模糊浮動視窗背後的畫面，觀感接近 Win11 工作列；關閉則只用系統壓克力",
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
        Add("LangSystem",
            "跟随系统", "跟隨系統", "Follow system",
            "Systemsprache", "システムに合わせる", "Как в системе",
            "Seguir al sistema", "Seguir o sistema");

        Add("RowUiFont",
            "界面字体", "介面字型", "UI font",
            "Schrift der Oberfläche", "画面のフォント", "Шрифт интерфейса",
            "Fuente de la interfaz", "Fonte da interface");
        Add("RowUiFontDesc",
            "默认字体已随程序分发，无需安装；换成系统里已装的字体立即生效。",
            "預設字型已隨程式分發，無需安裝；換成系統裡已安裝的字型立即生效。",
            "The default font ships with the app, so nothing needs installing. An installed font applies immediately.",
            "Die Standardschrift wird mitgeliefert und muss nicht installiert werden. Eine installierte Schrift greift sofort.",
            "既定のフォントはアプリに同梱されているため、インストールは不要です。インストール済みのフォントに変えるとすぐ反映されます。",
            "Шрифт по умолчанию входит в состав приложения и не требует установки. Установленный шрифт применяется сразу.",
            "La fuente predeterminada se incluye con la aplicación, así que no hay que instalar nada. Una fuente instalada se aplica al instante.",
            "A fonte padrão acompanha o aplicativo, sem precisar de instalação. Uma fonte instalada passa a valer na hora.");
        Add("FontChoiceDefault",
            "默认字体", "預設字型", "Default font",
            "Standardschrift", "既定のフォント", "Шрифт по умолчанию",
            "Fuente predeterminada", "Fonte padrão");

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
        Add("MenuTestLanguage",
            "输入法语言 / 中英文", "輸入法語言 / 中英文", "Input language / IME mode",
            "Eingabesprache / 中·EN", "入力言語 / 中・英", "Язык ввода / режим 中·EN",
            "Idioma de entrada / modo", "Idioma de entrada / modo");
        Add("ImeChinese",
            "中文输入", "中文輸入", "Chinese input",
            "Chinesische Eingabe", "中国語入力", "Ввод по-китайски",
            "Entrada en chino", "Entrada em chinês");
        Add("ImeEnglish",
            "英文输入", "英文輸入", "English input",
            "Englische Eingabe", "英語入力", "Ввод по-английски",
            "Entrada en inglés", "Entrada em inglês");
        Add("SampleTipOn",
            "大写锁定 开启", "大寫鎖定 開啟", "Caps Lock On",
            "Feststelltaste An", "Caps Lock オン", "Caps Lock Вкл",
            "Bloq Mayús Activado", "Caps Lock Ativado");

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

        Add("WelcomeTitle",
            "WinTips 已启用", "WinTips 已啟用", "WinTips is running",
            "WinTips ist aktiv", "WinTips を有効にしました", "WinTips включён",
            "WinTips está activo", "O WinTips está ativo");
        Add("WelcomeSubtitle",
            "在任务栏托盘点击图标即可打开设置", "在工作列系統匣點擊圖示即可開啟設定",
            "Click the tray icon to open settings",
            "Klicken Sie auf das Taskleisten-Symbol, um die Einstellungen zu öffnen",
            "タスクトレイのアイコンをクリックすると設定を開けます",
            "Щёлкните значок в области уведомлений, чтобы открыть параметры",
            "Haz clic en el icono de la bandeja para abrir la configuración",
            "Clique no ícone da bandeja para abrir as configurações");

        Add("LicenseTitle",
            "许可与致谢", "授權與致謝", "Licenses & credits",
            "Lizenzen & Danksagung", "ライセンスと謝辞", "Лицензии и благодарности",
            "Licencias y créditos", "Licenças e créditos");
        Add("LicenseSubtitle",
            "WinTips {0} · © 2026 NatureQ（自然卷），以 GPL-3.0 许可发布。界面字体 HarmonyOS Sans 随程序原样分发。",
            "WinTips {0} · © 2026 NatureQ（自然卷），以 GPL-3.0 授權釋出。介面字型 HarmonyOS Sans 隨程式原樣分送。",
            "WinTips {0} · © 2026 NatureQ, released under the GPL-3.0 license. The UI font HarmonyOS Sans ships unmodified with the app.",
            "WinTips {0} · © 2026 NatureQ, veröffentlicht unter der GPL-3.0-Lizenz. Die UI-Schrift HarmonyOS Sans wird unverändert mitgeliefert.",
            "WinTips {0} · © 2026 NatureQ（GPL-3.0 ライセンス）。UI フォント HarmonyOS Sans を無改変のまま同梱しています。",
            "WinTips {0} · © 2026 NatureQ, распространяется по лицензии GPL-3.0. Шрифт интерфейса HarmonyOS Sans поставляется без изменений.",
            "WinTips {0} · © 2026 NatureQ, publicado bajo la licencia GPL-3.0. La fuente de la interfaz HarmonyOS Sans se incluye sin modificar.",
            "WinTips {0} · © 2026 NatureQ, distribuído sob a licença GPL-3.0. A fonte da interface HarmonyOS Sans acompanha o app sem alterações.");
        Add("LicenseFontDesc",
            "随程序原样分发：字体文件未做任何修改（因此没有做子集裁剪），也不单独分发。",
            "隨程式原樣分送：字型檔未做任何修改（因此沒有做子集裁剪），也不單獨分送。",
            "Redistributed unmodified with the app: the font files are untouched (hence no subsetting) and are never distributed on their own.",
            "Unverändert mit der App verteilt: die Dateien bleiben unverändert (daher kein Subsetting) und werden nie separat verteilt.",
            "アプリに同梱して無改変のまま再配布しています（サブセット化は行わず、単体では配布しません）。",
            "Поставляется вместе с приложением без изменений: файлы шрифта не изменялись (поэтому без субсеттинга) и не распространяются отдельно.",
            "Se distribuye sin modificar junto con la aplicación: los archivos no se alteran (por eso no hay subsetting) y nunca se distribuyen por separado.",
            "Distribuída sem alterações junto com o aplicativo: os arquivos não são modificados (por isso sem subsetting) e nunca são distribuídos separadamente.");
        Add("LicenseCredit",
            "浮窗外观与动效参考 FluentFlyout（GPL-3.0）的 LockWindow 设计，代码为独立实现；本项目同样采用 GPL-3.0，与上游许可兼容。",
            "浮動視窗外觀與動效參考 FluentFlyout（GPL-3.0）的 LockWindow 設計，程式碼為獨立實作；本專案同樣採用 GPL-3.0，與上游授權相容。",
            "The flyout's look and motion take reference from the LockWindow design in FluentFlyout (GPL-3.0); the code here is an independent implementation, and WinTips is GPL-3.0 as well, so the two are license-compatible.",
            "Aussehen und Bewegung der Einblendung orientieren sich am LockWindow-Design von FluentFlyout (GPL-3.0); der Code ist eine eigenständige Umsetzung. WinTips steht selbst unter GPL-3.0, die Lizenzen sind also kompatibel.",
            "ポップアップの外観と動きは FluentFlyout（GPL-3.0）の LockWindow デザインを参考にしたもので、コードは独自実装です。WinTips 自体も GPL-3.0 のため、ライセンスは互換です。",
            "Внешний вид и анимация уведомления повторяют дизайн LockWindow из FluentFlyout (GPL-3.0); код — независимая реализация. Сам WinTips распространяется по GPL-3.0, лицензии совместимы.",
            "El aspecto y el movimiento de la ventana emergente se inspiran en el diseño LockWindow de FluentFlyout (GPL-3.0); el código es una implementación independiente. WinTips también usa GPL-3.0, por lo que las licencias son compatibles.",
            "O visual e o movimento da janela flutuante são inspirados no design LockWindow do FluentFlyout (GPL-3.0); o código é uma implementação independente. O WinTips também usa GPL-3.0, portanto as licenças são compatíveis.");
        Add("LicenseShowAgreement",
            "查看协议全文", "檢視協議全文", "Show full agreement",
            "Vollständige Lizenz anzeigen", "ライセンス全文を表示", "Показать полный текст",
            "Ver texto completo", "Ver texto completo");
        Add("LicenseHideAgreement",
            "收起协议全文", "收合協議全文", "Hide agreement",
            "Lizenz ausblenden", "ライセンス全文を隠す", "Скрыть полный текст",
            "Ocultar texto completo", "Ocultar texto completo");
        Add("LicenseOfficialLink",
            "官方原文 ↗", "官方原文 ↗", "Original text ↗",
            "Originaltext ↗", "原文 ↗", "Оригинал ↗",
            "Texto original ↗", "Texto original ↗");
        Add("LicenseTip",
            "点击查看字体许可与致谢全文", "點擊查看字型授權與致謝全文", "Click for the full font license and credits",
            "Klicken für die vollständige Schriftlizenz und Danksagung", "クリックでフォントライセンスと謝辞の全文を表示",
            "Нажмите, чтобы открыть полный текст лицензии и благодарности", "Haz clic para ver la licencia completa y los créditos",
            "Clique para ver a licença completa e os créditos");
        Add("LicenseClose",
            "关闭", "關閉", "Close",
            "Schließen", "閉じる", "Закрыть",
            "Cerrar", "Fechar");
        Add("GetLatestVersion",
            "获取最新版本", "取得最新版本", "Get latest version",
            "Neueste Version laden", "最新版を入手", "Скачать последнюю версию",
            "Obtener la última versión", "Obter a última versão");
        Add("AlreadyLatest",
            "已是最新版本", "已是最新版本", "Up to date",
            "Bereits aktuell", "最新バージョンです", "Уже последняя версия",
            "Ya está actualizado", "Já está atualizado");
        Add("RowCheckUpdates",
            "检查更新", "檢查更新", "Check for updates",
            "Nach Updates suchen", "更新を確認", "Проверять обновления",
            "Buscar actualizaciones", "Verificar atualizações");
        Add("RowCheckUpdatesDesc",
            "启动后每天最多查询一次 GitHub Releases，发现新版本时在下方「获取最新版本」旁点亮小红点。除此之外不联网。",
            "啟動後每天最多查詢一次 GitHub Releases，發現新版本時在下方「取得最新版本」旁亮起小紅點。除此之外不連網。",
            "Queries GitHub Releases at most once a day after launch; when a new version is found, a red dot lights up next to \"Get latest version\" below. No other network access.",
            "Prüft nach dem Start höchstens einmal täglich GitHub Releases; bei einer neuen Version leuchtet unten neben \"Neueste Version laden\" ein roter Punkt. Keine weiteren Netzwerkzugriffe.",
            "起動後 1 日 1 回まで GitHub Releases を確認し、新しいバージョンがあれば下の「最新版を入手」の横に赤い点を表示します。それ以外は通信しません。",
            "После запуска запрашивает GitHub Releases не чаще одного раза в сутки; при новой версии рядом с «Скачать последнюю версию» появится красная точка. Других обращений к сети нет.",
            "Consulta GitHub Releases como máximo una vez al día tras iniciarse; si hay una versión nueva, se ilumina un punto rojo junto a «Obtener la última versión». Sin otras conexiones.",
            "Consulta o GitHub Releases no máximo uma vez por dia após iniciar; se houver uma versão nova, um ponto vermelho acende ao lado de «Obter a última versão». Sem outras conexões.");
    }

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

    public static AppLanguage Language { get; private set; } = AppLanguage.System;

    public static AppLanguage Effective { get; private set; } = AppLanguage.ChineseSimplified;

    public static event Action? Changed;

    public static void Apply(AppLanguage language)
    {
        Language = language;
        Effective = language == AppLanguage.System ? DetectSystemLanguage() : language;

        TrSource.Instance.Refresh();
        Changed?.Invoke();
    }

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

    public static string T(string key)
    {
        if (!Table.TryGetValue(key, out var row)) return key;

        string text = row[IndexOf(Effective)];
        if (!string.IsNullOrEmpty(text)) return text;

        text = row[En];
        return string.IsNullOrEmpty(text) ? key : text;
    }

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);
}

public sealed class TrSource : INotifyPropertyChanged
{
    public static TrSource Instance { get; } = new();

    private TrSource() { }

    public string this[string key] => Loc.T(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Refresh() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
}

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
