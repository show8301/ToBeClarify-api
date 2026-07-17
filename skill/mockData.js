const image = (text, width = 1400, height = 900) => {
  const label = encodeURIComponent(text);
  return `https://imagefaker.access.mx.com/${width}x${height}/15151c/ff2e88/?text=${label}&font=noto`;
};

export const navigationItems = [
  { label: '首頁', href: '/home' },
  { label: '店員珍藏', href: '/staff' },
  { label: '艾歐澤亞週報', href: '/gallery' },
  { label: '佳餚名錄', href: '/menu' },
  { label: '留聲機', href: '/guestbook' },
  { label: '店舖動態', href: '/liveupdate' },
  {
    label: '榮譽殿堂',
    href: '/staffRanking',
    children: [
      { label: '店員榜', href: '/staffRanking' },
      { label: '消費榜', href: '/monetaryRanking' },
    ],
  },
];

export const shopInfo = {
  name: '35女王古殿',
  shortName: 'Twilight Salon',
  subtitle: '一處替旅人留燈的深夜貴賓廳，提供陪伴、談心、占卜與沉浸式 RP 接待。',
  businessStatus: '今日營業 21:30 開席',
  openHours: '每週五、六 21:30 - 24:00',
  server: 'Elemental / Aegis',
  address: '薰衣草苗圃 第 12 區 斜坡小屋',
  entryNote: '入場前請先閱讀消費說明與 RP 禮儀',
  about: [
    '35女王古殿是一間以深夜貴賓廳為主題的 FF14 RP 店，提供陪談、指名陪伴、活動企劃與角色沉浸式互動。',
    '我們位於 Elemental / Aegis，薰衣草苗圃第 12 區斜坡小屋，歡迎想放慢腳步的冒險者入席。',
    '店內重視彼此尊重與舒適節奏，第一次來訪也可以由店員協助說明入場、指名與拍照流程。',
  ],
  footerText: '燭火、杯影與低聲的故事，為每位來訪者保留一席安靜的夜晚。',
  heroImage: image('35女王古殿 Hero'),
  pricingNote: '第一版價格為 demo mock data，正式營運前可由後台或 API 維護。',
  pricing: [
    { name: '入場席費', price: '30,000 Gil' },
    { name: '指名陪伴', price: '50,000 Gil / 30min' },
    { name: '包廂預約', price: '120,000 Gil / 場' },
  ],
};

export const shopRules = [
  '店內全面禁止使用任何具有攻擊性、干擾他人 RP 體驗的技能與特效。',
  '點選女僕指名服務時，請尊重店員，嚴禁使用過度越軌的言詞或情感動作。',
  '拍照留念時請開啟 Gpose，並歡迎在社群平台上標註本店活動 Tag。',
  '若店內當前人數過多，包廂將採取計時 60 分鐘制，敬請冒險者配合。',
];

export const staffMembers = [
  {
    id: 'selene',
    nickname: '賽琳',
    role: '首席接待 / 占星談心',
    todayShift: '21:30 - 24:00',
    avatarUrl: image('賽琳 頭貼', 900, 1100),
    intro: '擅長安靜陪伴與夜談，會替旅人把混亂的心緒慢慢收束。',
    detail:
      '賽琳偏好低聲對話與角色內敘事，適合希望被溫柔接住、慢慢展開設定與心情的客人。她會先確認你的 RP 邊界，再安排談心節奏。',
    gallery: [
      image('賽琳 沙龍照', 1100, 1300),
      image('賽琳 生活照', 1200, 900),
      image('賽琳 夜談照', 1200, 900),
    ],
    commonServices: [
      { name: '迎賓陪談', price: '30,000 Gil' },
      { name: '雙人酒席', price: '45,000 Gil' },
      { name: '包廂陪伴', price: '120,000 Gil' },
    ],
    specialServices: [
      { name: '星盤短占', description: '以角色設定與當晚情境抽取短籤，延伸一段適合入戲的占星回饋。', price: '60,000 Gil' },
      { name: '角色背景共創', description: '陪同梳理角色過往、人際關係與入店動機，適合想補完設定的冒險者。', price: '80,000 Gil' },
    ],
  },
  {
    id: 'noir',
    nickname: '諾亞',
    role: '吧台侍者 / 劇情引導',
    todayShift: '22:00 - 24:00',
    avatarUrl: image('諾亞 頭貼', 900, 1100),
    intro: '語氣俐落、節奏明快，適合想要一場有火花對談的客人。',
    detail:
      '諾亞擅長把日常閒聊推進成一段完整小劇情，能配合酒館、偵探、舊識重逢等 RP 情境，也能替害羞的客人開場。',
    gallery: [
      image('諾亞 沙龍照', 1100, 1300),
      image('諾亞 吧台照', 1200, 900),
      image('諾亞 劇情照', 1200, 900),
    ],
    commonServices: [
      { name: '迎賓陪談', price: '30,000 Gil' },
      { name: '雙人酒席', price: '45,000 Gil' },
      { name: '包廂陪伴', price: '120,000 Gil' },
    ],
    specialServices: [
      { name: '即興劇情開場', description: '由店員替客人建立一段開場衝突或相遇橋段，降低第一次互動的尷尬感。', price: '70,000 Gil' },
      { name: '吧台秘飲 RP', description: '依角色心情調製一杯店內秘飲，並以飲品作為短篇對話主題。', price: '55,000 Gil' },
    ],
  },
  {
    id: 'lumi',
    nickname: '露米',
    role: '花廳陪伴 / 攝影協助',
    todayShift: '休假',
    avatarUrl: image('露米 頭貼', 900, 1100),
    intro: '輕快細膩，會替客人安排適合紀念照與短篇互動的氛圍。',
    detail:
      '露米熟悉店內光源與棚景，能帶客人完成一組角色紀念照，也適合想用短時間留下活動記憶的來訪者。',
    gallery: [
      image('露米 沙龍照', 1100, 1300),
      image('露米 棚拍照', 1200, 900),
      image('露米 紀念照', 1200, 900),
    ],
    commonServices: [
      { name: '迎賓陪談', price: '30,000 Gil' },
      { name: '雙人酒席', price: '45,000 Gil' },
      { name: '包廂陪伴', price: '120,000 Gil' },
    ],
    specialServices: [
      { name: '棚拍陪同', description: '陪同挑選店內棚景、協助安排站位與氛圍，適合留下完整活動紀念。', price: '75,000 Gil' },
      { name: '紀念照構圖', description: '提供雙人或多人合照構圖建議，讓照片更符合角色關係與場景故事。', price: '65,000 Gil' },
    ],
  },
  {
    id: 'ash',
    nickname: '亞修',
    role: '貴賓廳管家 / 禮儀引導',
    todayShift: '21:30 - 23:30',
    avatarUrl: image('亞修 頭貼', 900, 1100),
    intro: '沉穩、重視儀式感，擅長替初訪客人說明店內節奏。',
    detail:
      '亞修適合第一次接觸 RP 店的客人。他會清楚說明入場、點單、指名與互動邊界，讓整晚流程更穩定。',
    gallery: [
      image('亞修 沙龍照', 1100, 1300),
      image('亞修 貴賓廳', 1200, 900),
      image('亞修 導覽照', 1200, 900),
    ],
    commonServices: [
      { name: '迎賓陪談', price: '30,000 Gil' },
      { name: '雙人酒席', price: '45,000 Gil' },
      { name: '包廂陪伴', price: '120,000 Gil' },
    ],
    specialServices: [
      { name: '初訪導覽', description: '說明入場、指名、包廂與拍照流程，適合第一次接觸 RP 店的客人。', price: '40,000 Gil' },
      { name: '貴賓廳儀式 RP', description: '以正式迎賓、席間禮儀與送客橋段組成完整貴賓廳體驗。', price: '90,000 Gil' },
    ],
  },
];

export const events = [
  {
    id: 'moon-toast',
    title: '月下祝杯夜',
    summary: '以月光、低酒精調飲與雙人短談為主題的週末限定場。',
    period: '2026/07/03 - 2026/07/04',
    endAt: '2026-07-04',
    status: '生效中',
    imageUrl: image('月下祝杯夜'),
    details: [
      '活動期間每位來客可獲得一張月色籤詩，由當班店員依籤詩延伸一段短篇 RP。',
      '本場適合想輕量體驗陪談、初次入店或與朋友一起參與的客人。',
      '包廂名額有限，建議提前於店舖動態確認當週安排。',
    ],
  },
  {
    id: 'crystal-letter',
    title: '水晶留信企劃',
    summary: '替指定角色留下一封匿名短箋，活動結束後由店員朗讀或轉交。',
    period: '2026/06/21 - 2026/07/12',
    endAt: '2026-07-12',
    status: '生效中',
    imageUrl: image('水晶留信企劃'),
    details: [
      '客人可在店內填寫一封不含現實個資的角色短箋，指定公開朗讀或私下轉交。',
      '內容需符合店內 RP 禮儀，工作人員會保留審核與婉拒權利。',
    ],
  },
  {
    id: 'queen-tea-party',
    title: '女王花園茶會',
    summary: '以雙人茶席、輕量角色訪談與庭園拍照為主軸的午後系夜場。',
    period: '2026/07/18 - 2026/07/19',
    endAt: '2026-07-19',
    status: '未開始',
    imageUrl: image('女王花園茶會'),
    details: [
      '活動期間會開放花園主題茶席，店員會依來客角色設定安排一段短篇迎賓對話。',
      '本場適合想拍攝紀念照、體驗低壓互動，或與朋友一起入席的客人。',
      '茶席名額依當日店員人力調整，正式預約請以店舖動態公告為準。',
    ],
  },
  {
    id: 'midnight-oracle',
    title: '午夜神諭占卜夜',
    summary: '由當班店員以籤詩與角色提問延伸一段沉浸式占卜互動。',
    period: '2026/07/25 - 2026/07/26',
    endAt: '2026-07-26',
    status: '未開始',
    imageUrl: image('午夜神諭占卜夜'),
    details: [
      '來客可準備一個角色內問題，由店員抽取神諭籤並給予一段可延伸 RP 的回應。',
      '占卜內容以角色敘事與氣氛演出為主，不涉及現實人生、健康或財務判斷。',
      '若客人希望保留結果，可於活動後至留聲機留下角色回饋。',
    ],
  },
  {
    id: 'ember-ball',
    title: '燼火小舞會',
    summary: '已結束的舞會主題夜，保留給想回顧規則與照片的客人。',
    period: '2026/05/24 - 2026/05/25',
    endAt: '2026-05-25',
    status: '已失效',
    imageUrl: image('燼火小舞會'),
    details: [
      '燼火小舞會已結束，第一版 demo 保留一個失效活動案例。',
      '過期超過一個月的活動會在前端資料層排除，不再顯示於列表。',
    ],
  },
  {
    id: 'old-salon',
    title: '舊日沙龍夜',
    summary: '超過一個月的歷史活動，依規格不顯示於活動列表。',
    period: '2026/04/12 - 2026/04/13',
    endAt: '2026-04-13',
    status: '已失效',
    imageUrl: image('舊日沙龍夜'),
    details: ['這筆資料用於驗證過期活動不顯示。'],
  },
];

export const galleryItems = [
  {
    id: 'moon-toast-gallery',
    title: '月下祝杯夜',
    period: '2026/07/03 - 2026/07/04',
    endAt: '2026-07-04',
    description: '以月光、低酒精調飲與雙人短談為主題的週末限定花絮。',
    imageUrl: image('月下祝杯夜 花絮', 900, 700),
    details: [
      '本輯收錄月下祝杯夜的吧台佈置、座席燭光、店員互動與客人紀念照。',
      '照片以活動氛圍展示為主，正式上線後可接後台審核與公開狀態。',
    ],
    photos: [
      image('月下祝杯 01', 900, 1200),
      image('月下祝杯 02', 1200, 800),
      image('月下祝杯 03', 800, 1100),
      image('月下祝杯 04', 1100, 900),
      image('月下祝杯 05', 900, 900),
      image('月下祝杯 06', 1200, 1000),
      image('月下祝杯 07', 800, 1000),
      image('月下祝杯 08', 1100, 760),
    ],
  },
  {
    id: 'crystal-letter-gallery',
    title: '水晶留信企劃',
    period: '2026/06/21 - 2026/07/12',
    endAt: '2026-07-12',
    description: '匿名短箋、朗讀席與水晶燈影構成的活動紀錄。',
    imageUrl: image('水晶留信企劃 花絮', 900, 700),
    details: [
      '花絮聚焦留言卡、朗讀桌、店內水晶裝飾與活動期間的安靜角落。',
      '為避免暴露客人資訊，正式資料應只展示已審核的公開投稿。',
    ],
    photos: [
      image('水晶留信 01', 900, 1100),
      image('水晶留信 02', 1200, 780),
      image('水晶留信 03', 840, 1050),
      image('水晶留信 04', 1100, 1100),
      image('水晶留信 05', 900, 760),
      image('水晶留信 06', 1200, 900),
    ],
  },
  {
    id: 'ember-ball-gallery',
    title: '燼火小舞會',
    period: '2026/05/24 - 2026/05/25',
    endAt: '2026-05-25',
    description: '已超過一個月的活動相簿，依規格不顯示於列表。',
    imageUrl: image('燼火小舞會 花絮', 900, 700),
    details: ['這筆資料保留作為過期過一個月相簿不顯示的測試資料。'],
    photos: [image('燼火小舞會 01', 900, 700)],
  },
];

export const menuSections = [
  {
    id: 'sets',
    label: '套餐',
    intro: '適合初訪與雙人同行的完整席次，包含入席、飲品與一段店員陪伴。',
    items: [
      {
        id: 'moonlight-set',
        name: '月影迎賓套餐',
        description: '薰衣草庭園燉飯、玫瑰王冠小蛋糕與女王夜茶，適合初訪入席。',
        price: '78,000 Gil',
        imageUrl: image('月影迎賓套餐', 960, 720),
      },
      {
        id: 'queen-set',
        name: '女王古殿雙人席',
        description: '古殿香草烤肉盤、午夜莓果聖代與月下氣泡飲，適合雙人共享。',
        price: '95,000 Gil',
        imageUrl: image('女王古殿雙人席', 960, 720),
      },
      {
        id: 'crystal-set',
        name: '水晶祕語包廂',
        description: '星燈奶油鮮魚、水晶焦糖布丁與絲絨熱可可，適合安靜夜談。',
        price: '88,000 Gil',
        imageUrl: image('水晶祕語包廂', 960, 720),
      },
      {
        id: 'nocturne-set',
        name: '夜曲巡禮套餐',
        description: '星砂燉牛頰、月露奶酪與薰衣草冷泡茶，適合完整夜間巡禮。',
        price: '102,000 Gil',
        imageUrl: image('夜曲巡禮套餐', 960, 720),
      },
    ],
  },
  {
    id: 'mains',
    label: '主餐',
    intro: '以 RP 情境命名的主餐，適合搭配陪談、導覽或活動席次。',
    items: [
      {
        id: 'royal-roast',
        name: '古殿香草烤肉盤',
        description: '香草、焦糖洋蔥與熱湯搭配的經典主餐。',
        price: '45,000 Gil',
        imageUrl: image('古殿香草烤肉盤', 960, 720),
      },
      {
        id: 'starlight-fish',
        name: '星燈奶油鮮魚',
        description: '以低光餐席為靈感的柔和奶油魚排。',
        price: '42,000 Gil',
        imageUrl: image('星燈奶油鮮魚', 960, 720),
      },
      {
        id: 'garden-risotto',
        name: '薰衣草庭園燉飯',
        description: '花香蔬菜與白醬燉飯，適合安靜夜談。',
        price: '38,000 Gil',
        imageUrl: image('薰衣草庭園燉飯', 960, 720),
      },
      {
        id: 'nocturne-beef',
        name: '星砂燉牛頰',
        description: '慢燉牛頰、根莖蔬菜與微辛香料，適合完整晚席。',
        price: '52,000 Gil',
        imageUrl: image('星砂燉牛頰', 960, 720),
      },
      {
        id: 'rose-chicken',
        name: '玫瑰鹽烤雞',
        description: '以玫瑰鹽與香草烘烤，外皮微脆、肉汁溫潤。',
        price: '40,000 Gil',
        imageUrl: image('玫瑰鹽烤雞', 960, 720),
      },
      {
        id: 'midnight-pasta',
        name: '午夜黑松露麵',
        description: '黑松露奶油醬與細麵，適合搭配吧台短談。',
        price: '46,000 Gil',
        imageUrl: image('午夜黑松露麵', 960, 720),
      },
    ],
  },
  {
    id: 'desserts',
    label: '甜點',
    intro: '收尾用的小份甜點，偏向拍照、祝福與留言情境。',
    items: [
      {
        id: 'crown-cake',
        name: '玫瑰王冠小蛋糕',
        description: '粉紅玫瑰奶霜與金箔點綴的紀念甜點。',
        price: '28,000 Gil',
        imageUrl: image('玫瑰王冠小蛋糕', 960, 720),
      },
      {
        id: 'crystal-pudding',
        name: '水晶焦糖布丁',
        description: '透明糖片與深色焦糖，適合搭配留影。',
        price: '24,000 Gil',
        imageUrl: image('水晶焦糖布丁', 960, 720),
      },
      {
        id: 'midnight-parfait',
        name: '午夜莓果聖代',
        description: '莓果、奶霜與黑巧碎片堆疊的深夜甜品。',
        price: '32,000 Gil',
        imageUrl: image('午夜莓果聖代', 960, 720),
      },
      {
        id: 'moon-panna-cotta',
        name: '月露奶酪',
        description: '柔滑奶酪搭配透明糖露，口感清淡適合收尾。',
        price: '26,000 Gil',
        imageUrl: image('月露奶酪', 960, 720),
      },
      {
        id: 'velvet-tart',
        name: '絲絨巧克力塔',
        description: '濃厚巧克力餡與薄脆塔皮，適合深夜甜食客。',
        price: '30,000 Gil',
        imageUrl: image('絲絨巧克力塔', 960, 720),
      },
      {
        id: 'flower-macaron',
        name: '花廳馬卡龍組',
        description: '三色馬卡龍與花香糖霜，適合拍照留念。',
        price: '25,000 Gil',
        imageUrl: image('花廳馬卡龍組', 960, 720),
      },
    ],
  },
  {
    id: 'drinks',
    label: '飲品',
    intro: '吧台供應的無酒精與低酒精風味飲，作為陪談開場最穩定。',
    items: [
      {
        id: 'queen-tea',
        name: '女王夜茶',
        description: '黑茶、莓果與淡淡花香，適合初次入席。',
        price: '18,000 Gil',
        imageUrl: image('女王夜茶', 960, 720),
      },
      {
        id: 'moon-sparkle',
        name: '月下氣泡飲',
        description: '柑橘、蘇打與月色糖片，口感清爽。',
        price: '20,000 Gil',
        imageUrl: image('月下氣泡飲', 960, 720),
      },
      {
        id: 'velvet-cocoa',
        name: '絲絨熱可可',
        description: '厚奶泡與可可香氣，適合安靜收尾。',
        price: '22,000 Gil',
        imageUrl: image('絲絨熱可可', 960, 720),
      },
      {
        id: 'lavender-cold-brew',
        name: '薰衣草冷泡茶',
        description: '清爽茶底與淡淡花香，適合搭配長時間陪談。',
        price: '19,000 Gil',
        imageUrl: image('薰衣草冷泡茶', 960, 720),
      },
      {
        id: 'crystal-ade',
        name: '水晶檸檬露',
        description: '檸檬、蜂蜜與透明糖晶，酸甜明亮。',
        price: '21,000 Gil',
        imageUrl: image('水晶檸檬露', 960, 720),
      },
      {
        id: 'rose-latte',
        name: '玫瑰雲朵拿鐵',
        description: '玫瑰糖漿、綿密奶泡與低咖啡因基底。',
        price: '23,000 Gil',
        imageUrl: image('玫瑰雲朵拿鐵', 960, 720),
      },
    ],
  },
];

export const guestbookMessages = [
  {
    id: 'PIN-001',
    authorId: '暮光櫃台',
    message: '歡迎留下不含現實個資的 RP 感想。正式版會接審核流程，第一版僅作畫面展示。',
    createdAt: '2026-07-01T20:40:00+08:00',
    isPinned: true,
    replies: [
      {
        id: 'PIN-001-R01',
        authorId: '水晶櫃檯',
        message: '正式版會再加入審核與隱藏流程，demo 先保留公開展示。',
        createdAt: '2026-07-01T20:48:00+08:00',
      },
    ],
  },
  {
    id: 'PIN-002',
    authorId: '店內公告',
    message: '留言請保持角色內禮儀，避免攻擊性文字、過度越界內容或破壞他人沉浸體驗。',
    createdAt: '2026-07-01T20:30:00+08:00',
    isPinned: true,
    replies: [],
  },
  ...Array.from({ length: 18 }, (_, index) => {
    const number = index + 1;
    const day = String(30 - (index % 8)).padStart(2, '0');
    const hour = String(23 - (index % 5)).padStart(2, '0');
    const minute = String((index * 7) % 60).padStart(2, '0');
    return {
      id: `MSG-${String(number).padStart(3, '0')}`,
      authorId: ['匿名旅人', '白銀鄉常客', '月下來客', '初訪冒險者'][index % 4],
      message: [
        '第一次來也不會緊張，店員把流程說得很清楚，整晚節奏很舒服。',
        '喜歡這裡的低光佈置，適合把角色故事慢慢講完。',
        '月下祝杯的氣氛很漂亮，短籤詩讓角色有了新的靈感。',
        '包廂陪談節奏剛好，適合想安靜聊天的晚上。',
      ][index % 4],
      createdAt: `2026-06-${day}T${hour}:${minute}:00+08:00`,
      isPinned: false,
      replies:
        index % 5 === 0
          ? [
              {
                id: `MSG-${String(number).padStart(3, '0')}-R01`,
                authorId: '櫃檯回覆',
                message: '謝謝你的留言，期待下次再一起補完角色故事。',
                createdAt: `2026-06-${day}T${hour}:${String((Number(minute) + 8) % 60).padStart(2, '0')}:00+08:00`,
              },
            ]
          : [],
    };
  }),
];

export const liveUpdate = {
  lastUpdatedAt: '2026-07-01T22:18:36+08:00',
  scheduleStart: '20:00',
  scheduleEnd: '25:00',
  timeSlots: ['20:00', '20:30', '21:00', '21:30', '22:00', '22:30', '23:00', '23:30', '24:00', '24:30', '25:00'],
  reservations: [
    {
      id: 'RSV-001',
      staffId: 'selene',
      guestName: '白銀鄉常客',
      serviceName: '星盤短占',
      startAt: '21:30',
      endAt: '22:10',
      status: 'finished',
    },
    {
      id: 'RSV-002',
      staffId: 'selene',
      guestName: '水晶信使',
      serviceName: '角色背景共創',
      startAt: '22:30',
      endAt: '23:00',
      status: 'reserved',
    },
    {
      id: 'RSV-003',
      staffId: 'noir',
      guestName: '月下來客',
      serviceName: '即興劇情開場',
      startAt: '22:00',
      endAt: '22:45',
      status: 'active',
    },
    {
      id: 'RSV-004',
      staffId: 'ash',
      guestName: '匿名旅人',
      serviceName: '初訪導覽',
      startAt: '21:30',
      endAt: '22:45',
      status: 'active',
    },
    {
      id: 'RSV-005',
      staffId: 'ash',
      guestName: '銀鈴貴賓',
      serviceName: '貴賓廳儀式 RP',
      startAt: '23:00',
      endAt: '23:30',
      status: 'reserved',
    },
  ],
  staffStatuses: [
    { staffId: 'selene', status: 'available', label: '空閒中' },
    { staffId: 'noir', status: 'busy', label: '~22:45' },
    { staffId: 'lumi', status: 'off', label: '未上班' },
    { staffId: 'ash', status: 'busy', label: '~22:45' },
  ],
};

export const staffRankings = [
  { staffId: 'selene', nominations: 86, declaration: '願每一位旅人都能在夜裡找到被溫柔接住的位置。' },
  { staffId: 'noir', nominations: 73 },
  { staffId: 'ash', nominations: 61 },
  { staffId: 'lumi', nominations: 48 },
];

export const guestRankings = [
  { guestId: 'Moonlit-042', totalAmount: 1860000, title: '月冠貴賓' },
  { guestId: 'Aegis-Tea-17', totalAmount: 1520000, title: '水晶贊助者' },
  { guestId: 'Lavender-09', totalAmount: 1285000, title: '燭光守望者' },
  { guestId: 'Silent-Guest-31', totalAmount: 940000 },
  { guestId: 'SilverBell-22', totalAmount: 810000 },
  { guestId: 'Nocturne-58', totalAmount: 680000 },
];
