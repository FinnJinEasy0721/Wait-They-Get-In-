# -*- coding: utf-8 -*-
"""
Excel转Unity数据工具（保安模拟器·策划数据管线）
=================================================
功能：读取策划配置的Excel文件，转换为Unity可加载的JSON格式数据文件

使用方法：
  方式1（推荐）：双击运行此脚本，自动读取 Assets/GameData/Excel/ 下的Excel并输出JSON
  方式2（命令行）：python Excel转Unity数据工具.py <Excel目录> <JSON输出目录>

  Excel目录默认为：本项目 Assets/GameData/Excel/（策划数据统一放在这里）
  JSON输出目录默认为：本项目 Assets/GameData/Resources/GameData/

生成的JSON文件（与 GameDataJSON.cs 中的模型一一对应，表头必须一致）：
  - RuleConfig.json      -> 规则数据     （表头：Id/Name/Description/Category/SlotCost/CheckAttribute/SideEffect/RequestType/PassAttributes/FailAttributes）
  - BookConfig.json      -> 书籍数据     （表头：Id/DisplayName/RuleIds/MaxRefreshCount/CoverSpriteName）
  - VisitorConfig.json   -> 访客数据     （表头：Id/Name/Attributes/DayUnlock/Difficulty/Expression/BadgeAuthenticity/BadgeExpiry）
  - BehaviourConfig.json -> 访客行为库   （表头：Id/VisitorId/ActionName/TriggerRequest/TriggerCondition/ResponseText/HideAttributes）
  - GameConstants.json   -> 游戏常量     （第1列常量名，第2列值；输出为键值对列表）
  - ScheduleConfig.json  -> 7天日程编排  （第1~6列：天数/核心内容/访客ID/事件描述/玩家任务/特殊规则；G列聊天、H/I列报纸、J列群聊名称）
  - AttributeWeightConfig.json -> 属性权重表（表头：Tag/RecognizedWeight/UnrecognizedWeight/RecognizeSources）
      权重制判定核心表：每个标签的判定分数与"保安是否认识"的来源
      RecognizeSources 格式（分号分隔，可留空=天生认识）：
        装配:规则Id    —— 装着该规则时认识（如 装配:rule_002）
        新闻:第N天     —— 第N天起永久认识（该天必须有日程）
        投诉:标签      —— 当天有带该标签的访客愤怒离开或被警戒拦截，次日起学会
  - BranchConfig.json    -> 分支事件表   （宽表：一行=一个完整分支；表头：EventId/ConditionDay/Visitor1/Verdict1/
      Visitor2/Verdict2/DayState/EffectDay/AddVisitors/RemoveVisitors/ChatMessages/NewsTitle/NewsContent/Feedback）
      分支系统：某天结算结果满足条件时，改写另一天的聊天/报纸/访客日程/值班播报
      条件（全为"并且"关系）：Visitor1+Verdict1、Visitor2+Verdict2（访客级结果）、DayState（当天状态）
        Verdict 合法值：正确放行/错误放行/正确拦截/错误拦截/逼走/生气
        DayState 合法值：当天有访客被逼走/当天有访客被惹生气/当天拥挤
      效果（非空列生效）：AddVisitors(新增访客，排最前)/RemoveVisitors(删除访客)/
        ChatMessages(新聊天，整块替换)/NewsTitle+NewsContent(新报纸)/Feedback(值班记录提示一行)

Excel格式约定：
  - 每张表：第1行标题、第2行列说明、第3行英文表头（作为JSON键名，勿改）、第4行起为数据
  - 多值字段（RuleIds、Attributes、VisitorIds）用英文逗号分隔

在Unity中：
  JSON必须位于 Assets/GameData/Resources/GameData/ 目录（Resources文件夹内才能被加载），
  由 GameDataManager（挂载于场景）在Awake中通过 GameDataJSON 自动加载。
"""

import openpyxl
import json
import os
import sys


def read_sheet_data(ws):
    """读取worksheet的数据行（第3行表头，第4行起数据）"""
    if ws.max_row < 3:
        return []
    headers = []
    for col in range(1, ws.max_column + 1):
        val = ws.cell(row=3, column=col).value
        headers.append(str(val).strip() if val else f'col_{col}')

    rows = []
    for row in range(4, ws.max_row + 1):
        row_data = {}
        for col_idx, header in enumerate(headers, 1):
            val = ws.cell(row=row, column=col_idx).value
            if val is not None:
                row_data[header] = val
        if row_data:
            rows.append(row_data)
    return rows


def convert_list_field(entry, field):
    """把逗号分隔的多值列转为列表，空值转空列表"""
    val = entry.get(field, '')
    if isinstance(val, str):
        entry[field] = [v.strip() for v in val.replace('，', ',').split(',') if v.strip()]
    elif not isinstance(val, list):
        entry[field] = []


def convert_rule_config(excel_path, output_dir):
    wb = openpyxl.load_workbook(excel_path, data_only=True)
    rules = read_sheet_data(wb['规则数据'])
    wb.close()

    for r in rules:
        try:
            r['SlotCost'] = int(r.get('SlotCost', 1))
        except (ValueError, TypeError):
            r['SlotCost'] = 1
        r.setdefault('Category', '放行')
        r.setdefault('SideEffect', '无')
        r.setdefault('RequestType', '无')
        convert_list_field(r, 'PassAttributes')
        convert_list_field(r, 'FailAttributes')

    path = os.path.join(output_dir, 'RuleConfig.json')
    with open(path, 'w', encoding='utf-8') as f:
        json.dump({"rules": rules}, f, ensure_ascii=False, indent=2)
    print(f"  [OK] RuleConfig.json -> {path} ({len(rules)}条)")
    return rules


def convert_book_config(excel_path, output_dir):
    wb = openpyxl.load_workbook(excel_path, data_only=True)
    books = read_sheet_data(wb['书籍数据'])
    wb.close()

    for b in books:
        b.setdefault('RuleIds', '')
        if isinstance(b.get('RuleIds'), str):
            b['RuleIds'] = [r.strip() for r in b['RuleIds'].replace('，', ',').split(',') if r.strip()]
        else:
            b['RuleIds'] = []
        try:
            b['MaxRefreshCount'] = int(b.get('MaxRefreshCount', 3))
        except (ValueError, TypeError):
            b['MaxRefreshCount'] = 3
        b.setdefault('CoverSpriteName', '')  # 可选：封面图名，从 Resources/GameData/BookCovers/ 加载

    path = os.path.join(output_dir, 'BookConfig.json')
    with open(path, 'w', encoding='utf-8') as f:
        json.dump({"books": books}, f, ensure_ascii=False, indent=2)
    print(f"  [OK] BookConfig.json -> {path} ({len(books)}本)")
    return books


def convert_visitor_config(excel_path, output_dir):
    wb = openpyxl.load_workbook(excel_path, data_only=True)
    visitors = read_sheet_data(wb['访客数据'])
    wb.close()

    for v in visitors:
        v.setdefault('Attributes', '')
        if isinstance(v.get('Attributes'), str):
            v['Attributes'] = [a.strip() for a in v['Attributes'].replace('，', ',').split(',') if a.strip()]
        else:
            v['Attributes'] = []
        try:
            v['DayUnlock'] = int(v.get('DayUnlock', 1))
        except (ValueError, TypeError):
            v['DayUnlock'] = 1
        try:
            v['Difficulty'] = int(v.get('Difficulty', 1))
        except (ValueError, TypeError):
            v['Difficulty'] = 1
        # 第二阶段新列：表情/工牌真假/证件时效（空单元格转"无"）
        v.setdefault('Expression', '无')
        v.setdefault('BadgeAuthenticity', '无')
        v.setdefault('BadgeExpiry', '无')
        # 标准处置列：放行/拦截（空=运行时按小偷特征推导）
        v['CorrectOutcome'] = str(v.get('CorrectOutcome') or '').strip()

    path = os.path.join(output_dir, 'VisitorConfig.json')
    with open(path, 'w', encoding='utf-8') as f:
        json.dump({"visitors": visitors}, f, ensure_ascii=False, indent=2)
    print(f"  [OK] VisitorConfig.json -> {path} ({len(visitors)}个)")
    return visitors


def convert_behaviour_config(excel_path, output_dir):
    """访客行为库：每个访客的行为与触发条件（原型文档：行为库模块）"""
    wb = openpyxl.load_workbook(excel_path, data_only=True)
    behaviours = read_sheet_data(wb['行为库'])
    wb.close()

    for b in behaviours:
        b.setdefault('ActionName', '')
        b.setdefault('TriggerRequest', '无')
        b.setdefault('TriggerCondition', '')
        b.setdefault('ResponseText', '')
        convert_list_field(b, 'HideAttributes')

    path = os.path.join(output_dir, 'BehaviourConfig.json')
    with open(path, 'w', encoding='utf-8') as f:
        json.dump({"behaviours": behaviours}, f, ensure_ascii=False, indent=2)
    print(f"  [OK] BehaviourConfig.json -> {path} ({len(behaviours)}条)")
    return behaviours


def convert_attribute_weight_config(excel_path, output_dir):
    """属性权重表：标签判定权重（权重制核心表）
    Tag=标签白名单词；RecognizedWeight=识别时权重；UnrecognizedWeight=未识别时权重（留空=运行时用常量"未识别默认权重"）；
    RecognizeSources=识别来源（装配:规则Id / 新闻:第N天 / 投诉:标签，分号分隔，留空=天生认识）"""
    wb = openpyxl.load_workbook(excel_path, data_only=True)
    weights = read_sheet_data(wb['属性权重表'])
    wb.close()

    for w in weights:
        try:
            w['RecognizedWeight'] = int(float(str(w.get('RecognizedWeight', 0)).strip()))
        except (ValueError, TypeError):
            w['RecognizedWeight'] = 0
        # 未识别权重统一转字符串：空=留空（运行时用常量兜底），非空=可解析数字
        raw_unrec = w.get('UnrecognizedWeight')
        if raw_unrec is None or str(raw_unrec).strip() == '':
            w['UnrecognizedWeight'] = ''
        else:
            w['UnrecognizedWeight'] = str(raw_unrec).strip()
        # 来源统一为字符串并清理中文分号
        sources = str(w.get('RecognizeSources') or '').strip()
        w['RecognizeSources'] = sources.replace('；', ';')

    path = os.path.join(output_dir, 'AttributeWeightConfig.json')
    with open(path, 'w', encoding='utf-8') as f:
        json.dump({"weights": weights}, f, ensure_ascii=False, indent=2)
    print(f"  [OK] AttributeWeightConfig.json -> {path} ({len(weights)}条)")
    return weights


def convert_game_constants(excel_path, output_dir):
    wb = openpyxl.load_workbook(excel_path, data_only=True)
    ws = wb['游戏常量']
    # 输出为键值对列表：Unity的JsonUtility不支持字典，GameDataJSON.ConstantEntry 为 {key, value}
    constants = []
    for row in range(4, ws.max_row + 1):
        key = ws.cell(row=row, column=1).value
        value = ws.cell(row=row, column=2).value
        if key is None or str(key).strip() == '':
            continue
        key_str = str(key).strip()
        # 值统一转字符串存储（数字由C#端按需转int/float）
        constants.append({'key': key_str, 'value': str(value).strip() if value is not None else ''})
    wb.close()

    path = os.path.join(output_dir, 'GameConstants.json')
    with open(path, 'w', encoding='utf-8') as f:
        json.dump({"constants": constants}, f, ensure_ascii=False, indent=2)
    print(f"  [OK] GameConstants.json -> {path} ({len(constants)}个)")
    return path


def parse_chat_messages(raw):
    """把"发送者:内容；发送者:内容"格式的单元格解析为消息列表（没冒号的消息归为"通知"）"""
    messages = []
    raw = str(raw or '').strip()
    if not raw:
        return messages
    for msg in raw.replace('；', ';').split(';'):
        msg = msg.strip()
        if not msg:
            continue
        if ':' in msg:
            sender, text = msg.split(':', 1)
            messages.append({'Sender': sender.strip(), 'Text': text.strip()})
        else:
            messages.append({'Sender': '通知', 'Text': msg})
    return messages


def convert_schedule_config(excel_path, output_dir):
    wb = openpyxl.load_workbook(excel_path, data_only=True)
    ws = wb['7天日程编排']
    schedule = []
    for row in range(4, ws.max_row + 1):
        day = ws.cell(row=row, column=1).value
        if day is None:
            continue
        day_str = str(day).strip()
        if not day_str.startswith('第') or not day_str.endswith('天'):
            continue

        visitor_ids_raw = ws.cell(row=row, column=3).value
        visitor_ids = []
        if visitor_ids_raw:
            visitor_ids = [v.strip() for v in str(visitor_ids_raw).replace('，', ',').split(',') if v.strip()]

        # 手机聊天记录（G列）：多条用中文分号"；"分隔，每条"发送者:内容"（ReadNews场景显示）
        chat_messages = parse_chat_messages(ws.cell(row=row, column=7).value)

        # 新闻报纸（H/I列）：标题+正文（ReadNews场景显示）
        news_title = str(ws.cell(row=row, column=8).value or '').strip()
        news_content = str(ws.cell(row=row, column=9).value or '').strip()

        # 群聊名称（J列）：ReadNews聊天面板顶部显示的群名
        chat_group_name = str(ws.cell(row=row, column=10).value or '').strip()

        schedule.append({
            'Day': day_str,
            'CoreContent': str(ws.cell(row=row, column=2).value or ''),
            'VisitorIds': visitor_ids,
            'EventDescription': str(ws.cell(row=row, column=4).value or ''),
            'PlayerTask': str(ws.cell(row=row, column=5).value or ''),
            'SpecialRule': str(ws.cell(row=row, column=6).value or '无'),
            'ChatMessages': chat_messages,
            'NewsTitle': news_title,
            'NewsContent': news_content,
            'ChatGroupName': chat_group_name,
        })
    wb.close()

    path = os.path.join(output_dir, 'ScheduleConfig.json')
    with open(path, 'w', encoding='utf-8') as f:
        json.dump({"schedule": schedule}, f, ensure_ascii=False, indent=2)
    print(f"  [OK] ScheduleConfig.json -> {path} ({len(schedule)}天)")
    return schedule


def convert_branch_config(excel_path, output_dir):
    """分支事件表（宽表）：一行=一个完整分支
    条件（并且关系）：Visitor1+Verdict1 / Visitor2+Verdict2 / DayState；效果：非空列生效"""
    wb = openpyxl.load_workbook(excel_path, data_only=True)
    branches = read_sheet_data(wb['分支事件表'])
    wb.close()

    for b in branches:
        for key in ('EventId', 'ConditionDay', 'EffectDay', 'Visitor1', 'Verdict1',
                    'Visitor2', 'Verdict2', 'DayState', 'NewsTitle', 'NewsContent', 'Feedback'):
            b[key] = str(b.get(key) or '').strip()
        b.pop('Remark', None)   # 备注列不导出

        # 新增/删除访客：逗号分隔（兼容中文逗号）→ 列表
        for key in ('AddVisitors', 'RemoveVisitors'):
            b[key] = [v.strip() for v in str(b.get(key) or '').replace('，', ',').split(',') if v.strip()]

        # 新聊天记录：多条"发送者:内容"分号分隔
        b['ChatMessages'] = parse_chat_messages(b.get('ChatMessages'))

    path = os.path.join(output_dir, 'BranchConfig.json')
    with open(path, 'w', encoding='utf-8') as f:
        json.dump({"branches": branches}, f, ensure_ascii=False, indent=2)
    print(f"  [OK] BranchConfig.json -> {path} ({len(branches)}条)")
    return branches


def main():
    print("=" * 60)
    print("  保安模拟器 - Excel转Unity数据工具")
    print("=" * 60)

    # 本脚本位于 Assets/GameData/ 根目录：Excel/ 放表，Resources/GameData/ 放生成的JSON
    script_dir = os.path.dirname(os.path.abspath(__file__))
    default_excel = os.path.join(script_dir, 'Excel')
    default_output = os.path.join(script_dir, 'Resources', 'GameData')

    if len(sys.argv) > 2:
        excel_dir = sys.argv[1]
        output_dir = sys.argv[2]
    elif len(sys.argv) > 1:
        excel_dir = sys.argv[1]
        output_dir = default_output
    else:
        excel_dir = default_excel
        output_dir = default_output

    os.makedirs(output_dir, exist_ok=True)

    print(f"\n  Excel目录: {excel_dir}")
    print(f"  输出目录:  {output_dir}")
    print()

    files = [
        ('规则数据', '规则数据.xlsx', convert_rule_config),
        ('书籍数据', '书籍数据.xlsx', convert_book_config),
        ('访客数据', '访客数据.xlsx', convert_visitor_config),
        ('行为库', '访客行为库.xlsx', convert_behaviour_config),
        ('游戏常量', '游戏常量.xlsx', convert_game_constants),
        ('7天日程', '7天日程编排.xlsx', convert_schedule_config),
    ]

def validate_data(rules, books, visitors, behaviours, schedule, weights, branches):
    """交叉校验（与Unity运行时校验对齐，让策划在导出阶段就发现引用错误）。
    返回错误列表，空列表=全部正确。"""
    errors = []

    rule_ids = {r.get('Id') for r in rules if r.get('Id')}
    visitor_ids = {v.get('Id') for v in visitors if v.get('Id')}

    # 合法值定义（与 C# 枚举对齐）
    valid_categories = {'放行', '请示', '警戒'}
    valid_requests = {'请出示工牌', '请勿遮挡时间信息', '请示上级', '请求切换', '询问姓名', '询问曾用名', '请说普通话', '无'}
    valid_trigger_requests = {'请出示工牌', '请勿遮挡时间信息', '放行', '请示上级', '请求切换', '询问姓名', '询问曾用名', '请说普通话', '无'}
    valid_attributes = {
        '有工牌', '无工牌', '有制服', '无制服', '在名单内', '在名单外',
        '口音正常', '口音异常', '行为正常', '行为异常', '持证', '证件过期',
        '声称认识老板', '笑容', '沮丧', '心虚', '工牌为真', '工牌为假', '证件有效',
        '新样式工牌', '完成访客登记', '未完成登记', '完成登记',
    }
    valid_conditions = ('需属性:', '计数:懵逼>=', '计数:请求>=', '状态:愤怒')
    schedule_days = {day.get('Day') for day in schedule if day.get('Day')}

    # 1) 规则表：类别与请求类型合法值
    for r in rules:
        rid = r.get('Id', '?')
        if r.get('Category') not in valid_categories:
            errors.append(f"规则表 {rid}: 类别 \"{r.get('Category')}\" 不合法（只能填 放行/请示/警戒）")
        req = str(r.get('RequestType') or '无').strip()
        if req not in valid_requests:
            errors.append(f"规则表 {rid}: 发出请求 \"{req}\" 不合法（只能填 请出示工牌/请勿遮挡时间信息/请示上级/请求切换/询问姓名/询问曾用名/请说普通话/无）")
        for col in ('PassAttributes', 'FailAttributes'):
            for attr in r.get(col) or []:
                if attr not in valid_attributes:
                    errors.append(f"规则表 {rid}: {col} 中的属性 \"{attr}\" 不在合法属性列表内")

    # 2) 书籍表：RuleIds 必须已存在于规则表
    for b in books:
        bid = b.get('Id', '?')
        for rid in b.get('RuleIds') or []:
            if rid not in rule_ids:
                errors.append(f"书籍表 {bid}: 引用了不存在的规则ID \"{rid}\"")
        if not (b.get('RuleIds') or []):
            errors.append(f"书籍表 {bid}: 规则池为空（保安无法从此书获得任何规则）")

    # 3) 访客表：属性标签合法值 + Id 去重
    seen = set()
    for v in visitors:
        vid = v.get('Id', '?')
        if vid in seen:
            errors.append(f"访客表: 访客ID \"{vid}\" 重复")
        seen.add(vid)
        for attr in v.get('Attributes') or []:
            if attr not in valid_attributes:
                errors.append(f"访客表 {vid}: 属性 \"{attr}\" 不在合法属性列表内")
        # 标准处置：只能填 放行/拦截（留空=运行时按小偷特征推导，提示策划显式配置）
        outcome = str(v.get('CorrectOutcome') or '').strip()
        if outcome == '':
            errors.append(f"访客表 {vid}: 未填标准处置 CorrectOutcome（留空将按小偷特征[心虚+证件过期+有工牌]推导，建议显式填 放行/拦截）")
        elif outcome not in ('放行', '拦截'):
            errors.append(f"访客表 {vid}: 标准处置 \"{outcome}\" 不合法（只能填 放行/拦截）")

    # 4) 行为库：VisitorId 必须已存在于访客表；请求与条件语法
    for b in behaviours:
        bid = b.get('Id', '?')
        if b.get('VisitorId') not in visitor_ids:
            errors.append(f"行为库 {bid}: 所属访客 \"{b.get('VisitorId')}\" 不在访客数据表中")
        trig = str(b.get('TriggerRequest') or '无').strip()
        if trig not in valid_trigger_requests:
            errors.append(f"行为库 {bid}: 触发请求 \"{trig}\" 不合法（只能填 请出示工牌/请勿遮挡时间信息/放行/请示上级/请求切换/询问姓名/询问曾用名/请说普通话/无）")
        cond = str(b.get('TriggerCondition') or '').strip()
        if cond and not cond.startswith(valid_conditions):
            errors.append(f"行为库 {bid}: 触发条件 \"{cond}\" 语法不合法（应为空 或 需属性:标签 / 计数:懵逼>=N / 计数:请求>=N / 状态:愤怒）")

    # 5) 日程表：VisitorIds 必须已存在于访客表
    for day in schedule:
        d = day.get('Day', '?')
        for vid in day.get('VisitorIds') or []:
            if vid not in visitor_ids:
                errors.append(f"日程表 {d}: 引用了不存在的访客ID \"{vid}\"（注意别漏下划线，如 visitor_011）")

    # 6) 日程表：聊天/报纸提示完整性（只警告级提示：没配当天聊天/报纸则ReadNews场景无内容可看）
    for day in schedule:
        d = day.get('Day', '?')
        has_chat = bool(day.get('ChatMessages'))
        has_news = bool(day.get('NewsTitle'))
        if not has_chat and not has_news:
            errors.append(f"日程表 {d}: 聊天记录与报纸标题都为空，玩家在ReadNews场景将无内容可看")
        # 配了聊天但没配群聊名称 → 聊天面板顶部群名走代码兜底"工作群"，提示策划补配
        if has_chat and not str(day.get('ChatGroupName') or '').strip():
            errors.append(f"日程表 {d}: 配了聊天记录但未填群聊名称（J列 ChatGroupName），面板顶部将显示默认名")

    # 7) 属性权重表：标签合法值/重复、来源语法与跨表引用
    weight_tags = set()
    for w in weights:
        tag = str(w.get('Tag') or '').strip()
        if not tag:
            errors.append("属性权重表: 存在Tag为空的行")
            continue
        if tag not in valid_attributes:
            errors.append(f"属性权重表: 标签 \"{tag}\" 不在合法属性列表内（需先扩充C#枚举再填表）")
            continue
        if tag in weight_tags:
            errors.append(f"属性权重表: 标签 \"{tag}\" 重复配置")
        weight_tags.add(tag)

        # 未识别权重：留空合法（用常量兜底），非空必须是数字
        unrec = str(w.get('UnrecognizedWeight') or '').strip()
        if unrec:
            try:
                float(unrec)
            except ValueError:
                errors.append(f"属性权重表 {tag}: 未识别时权重 \"{unrec}\" 不是数字（留空=用常量\"未识别默认权重\"）")

        # 来源语法：装配:规则Id / 新闻:第N天 / 投诉:标签
        sources = str(w.get('RecognizeSources') or '').strip()
        if sources:
            for part in sources.split(';'):
                part = part.strip()
                if not part:
                    continue
                if part.startswith('装配:'):
                    rid = part[len('装配:'):].strip()
                    if not rid:
                        errors.append(f"属性权重表 {tag}: 来源 \"{part}\" 缺少规则ID")
                    elif rid not in rule_ids:
                        errors.append(f"属性权重表 {tag}: 来源引用了不存在的规则ID \"{rid}\"")
                elif part.startswith('新闻:'):
                    day_label = part[len('新闻:'):].strip()
                    normalized = day_label if day_label.startswith('第') else f"第{day_label}天"
                    if normalized not in schedule_days:
                        errors.append(f"属性权重表 {tag}: 来源引用的日程 \"{day_label}\" 不在7天日程表中")
                elif part.startswith('投诉:'):
                    trig = part[len('投诉:'):].strip()
                    if not trig:
                        errors.append(f"属性权重表 {tag}: 来源 \"{part}\" 缺少触发标签")
                    elif trig not in valid_attributes:
                        errors.append(f"属性权重表 {tag}: 投诉触发标签 \"{trig}\" 不在合法属性列表内")
                else:
                    errors.append(f"属性权重表 {tag}: 来源 \"{part}\" 语法不合法（应为 装配:规则Id / 新闻:第N天 / 投诉:标签）")

    # 8) 权重覆盖完整性：访客身上出现过的标签（含三列合并产生的），权重表里必须有对应行（缺失=按0分计）
    used_tags = set()
    for v in visitors:
        for attr in v.get('Attributes') or []:
            used_tags.add(attr)
    merged_extras = {'笑容', '沮丧', '心虚', '工牌为真', '工牌为假', '证件有效', '证件过期'}
    for attr in (used_tags | merged_extras):
        if attr in valid_attributes and attr not in weight_tags:
            errors.append(f"属性权重表: 标签 \"{attr}\" 被访客使用但未配置权重行（运行时按0分计）")

    # 9) 分支事件表（宽表）：条件配对完整性/判定值/天数先后/效果非空与跨表引用
    valid_verdicts = {'正确放行', '错误放行', '正确拦截', '错误拦截', '逼走', '生气'}
    valid_states = {'当天有访客被逼走', '当天有访客被惹生气', '当天拥挤'}
    seen_event_ids = set()
    schedule_visitor_map = {day.get('Day'): list(day.get('VisitorIds') or []) for day in schedule}

    def day_number_of(label):
        digits = ''.join(ch for ch in str(label) if ch.isdigit())
        return int(digits) if digits else None

    for b in branches:
        eid = b.get('EventId') or '(空)'
        if not b.get('EventId'):
            errors.append("分支表: 存在EventId为空的行")
        elif eid in seen_event_ids:
            errors.append(f"分支表 {eid}: EventId 重复")
        seen_event_ids.add(eid)

        cday, eday = b.get('ConditionDay'), b.get('EffectDay')
        cnum, eday_num = day_number_of(cday), day_number_of(eday)
        if cday not in schedule_days:
            errors.append(f"分支表 {eid}: 条件日 \"{cday}\" 不在7天日程表中（应填 第N天）")
        if eday not in schedule_days:
            errors.append(f"分支表 {eid}: 生效日 \"{eday}\" 不在7天日程表中（应填 第N天）")
        if cnum is not None and eday_num is not None and eday_num <= cnum:
            errors.append(f"分支表 {eid}: 生效日 \"{eday}\" 必须晚于条件日 \"{cday}\"")

        # 条件配对完整性：访客与结果成对出现；访客2不能脱离访客1单独存在
        v1, r1 = b.get('Visitor1'), b.get('Verdict1')
        v2, r2 = b.get('Visitor2'), b.get('Verdict2')
        state = b.get('DayState')
        if v1 and not r1:
            errors.append(f"分支表 {eid}: 访客1填了 \"{v1}\" 但结果1没填")
        if r1 and not v1:
            errors.append(f"分支表 {eid}: 结果1填了 \"{r1}\" 但访客1没填")
        if v2 and not r2:
            errors.append(f"分支表 {eid}: 访客2填了 \"{v2}\" 但结果2没填")
        if r2 and not v2:
            errors.append(f"分支表 {eid}: 结果2填了 \"{r2}\" 但访客2没填")
        if (v2 or r2) and not (v1 and r1):
            errors.append(f"分支表 {eid}: 第二个条件不能脱离访客1+结果1单独存在")
        for vid, rid in ((v1, r1), (v2, r2)):
            if vid and vid not in visitor_ids:
                errors.append(f"分支表 {eid}: 条件访客 \"{vid}\" 不在访客数据表中")
            if rid and rid not in valid_verdicts:
                errors.append(f"分支表 {eid}: 结果 \"{rid}\" 不合法（只能填 {'/'.join(sorted(valid_verdicts))}）")
        if state and state not in valid_states:
            errors.append(f"分支表 {eid}: 当天状态 \"{state}\" 不合法（只能填 {'/'.join(sorted(valid_states))}，留空=不要求）")
        if not (v1 or v2 or state):
            errors.append(f"分支表 {eid}: 一个触发条件都没有（至少填 访客1+结果1 或 当天状态）")

        # 效果完整性：至少一列生效；报纸标题正文成对
        has_chat = bool(b.get('ChatMessages'))
        has_news = bool(b.get('NewsTitle')) or bool(b.get('NewsContent'))
        has_visitors = bool(b.get('AddVisitors')) or bool(b.get('RemoveVisitors'))
        has_feedback = bool(b.get('Feedback'))
        if not (has_chat or has_news or has_visitors or has_feedback):
            errors.append(f"分支表 {eid}: 没有任何效果（新增/删除访客、新聊天、新报纸、值班记录提示至少填一项）")
        if bool(b.get('NewsTitle')) != bool(b.get('NewsContent')):
            errors.append(f"分支表 {eid}: 新报纸的标题与正文必须成对填写（只填一个=报纸不生效）")

        # 新增/删除访客跨表引用
        for vid in b.get('AddVisitors') or []:
            if vid not in visitor_ids:
                errors.append(f"分支表 {eid}: 新增访客 \"{vid}\" 不在访客数据表中")
            if vid in schedule_visitor_map.get(eday, []):
                errors.append(f"分支表 {eid}: 新增访客 \"{vid}\" 已在 {eday} 原日程中（当天会重复出场，请确认）")
        for vid in b.get('RemoveVisitors') or []:
            if vid not in visitor_ids:
                errors.append(f"分支表 {eid}: 删除访客 \"{vid}\" 不在访客数据表中")

    return errors


def main():
    print("=" * 60)
    print("  保安模拟器 - Excel转Unity数据工具")
    print("=" * 60)

    # 本脚本位于 Assets/GameData/ 根目录：Excel/ 放表，Resources/GameData/ 放生成的JSON
    script_dir = os.path.dirname(os.path.abspath(__file__))
    default_excel = os.path.join(script_dir, 'Excel')
    default_output = os.path.join(script_dir, 'Resources', 'GameData')

    if len(sys.argv) > 2:
        excel_dir = sys.argv[1]
        output_dir = sys.argv[2]
    elif len(sys.argv) > 1:
        excel_dir = sys.argv[1]
        output_dir = default_output
    else:
        excel_dir = default_excel
        output_dir = default_output

    os.makedirs(output_dir, exist_ok=True)

    print(f"\n  Excel目录: {excel_dir}")
    print(f"  输出目录:  {output_dir}")
    print()

    files = [
        ('规则数据', '规则数据.xlsx', convert_rule_config),
        ('书籍数据', '书籍数据.xlsx', convert_book_config),
        ('访客数据', '访客数据.xlsx', convert_visitor_config),
        ('行为库', '访客行为库.xlsx', convert_behaviour_config),
        ('游戏常量', '游戏常量.xlsx', convert_game_constants),
        ('7天日程', '7天日程编排.xlsx', convert_schedule_config),
        ('属性权重表', '属性权重表.xlsx', convert_attribute_weight_config),
        ('分支事件表', '分支事件表.xlsx', convert_branch_config),
    ]

    ok = 0
    converted = {}   # 收集各表数据用于交叉校验
    failed = False
    for name, fname, func in files:
        fpath = os.path.join(excel_dir, fname)
        if os.path.exists(fpath):
            try:
                converted[name] = func(fpath, output_dir)
                ok += 1
            except Exception as e:
                failed = True
                print(f"  [FAIL] {name}: {e}")
        else:
            failed = True
            print(f"  [SKIP] {name} ({fname}) - 未找到")

    print(f"\n{'=' * 60}")

    if failed:
        print(f"  × 导出未完成：仅 {ok}/{len(files)} 张表转换成功，请检查上方 [FAIL]/[SKIP] 项")
        print(f"{'=' * 60}")
        _pause_if_interactive()
        sys.exit(1)

    # 跨表交叉校验：引用完整性 + 合法值
    errors = validate_data(
        converted.get('规则数据') or [],
        converted.get('书籍数据') or [],
        converted.get('访客数据') or [],
        converted.get('行为库') or [],
        converted.get('7天日程') or [],
        converted.get('属性权重表') or [],
        converted.get('分支事件表') or [],
    )

    if errors:
        print(f"  × 导出成功但数据校验发现 {len(errors)} 处问题（Unity内也可能运行异常）：")
        for e in errors:
            print(f"     - {e}")
        print(f"{'=' * 60}")
        _pause_if_interactive()
        sys.exit(1)

    print(f"  √ 全部导出正确：{ok}/{len(files)} 张表转换成功，数据校验通过")
    print(f"     JSON位于 Assets/GameData/Resources/GameData/，回Unity即可生效")
    print(f"{'=' * 60}")
    _pause_if_interactive()
    sys.exit(0)


def _pause_if_interactive():
    """双击运行时暂停窗口防闪退（命令行/脚本调用不暂停）"""
    try:
        if sys.stdin is not None and sys.stdin.isatty():
            input("\n  按回车键关闭窗口...")
    except Exception:
        pass


if __name__ == '__main__':
    main()
