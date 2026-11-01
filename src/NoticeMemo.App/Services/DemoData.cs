using NoticeMemo.Core.Models;
using NoticeMemo.Core.Services;
using NoticeMemo.Core.Storage;

namespace NoticeMemo.App.Services;

public static class DemoData
{
    public static void Seed(IMemoStore store)
    {
        var now = DateTimeOffset.Now;
        var notices = new[]
        {
            Parse($"""
                @全体成员
                【教务通知】关于本学期期中考试安排的通知
                各位同学：
                本学期期中考试定于{now.AddDays(2):M月d日} 14:30在教学楼A302举行。
                请务必于今天17:00前完成报名确认，逾期不再受理。
                详情：https://example.edu.cn/exam
                #考试 #报名
                """, MemoStatus.Active),
            Parse($"""
                【讲座预告】人工智能与未来教育
                本周三晚上7:00在图书馆报告厅举行专题讲座，欢迎同学们参加。
                请提前10分钟入场，座位有限。
                #讲座 #活动
                """, MemoStatus.Inbox),
            Parse($"""
                关于秋季志愿活动报名的通知
                本周六上午9:00在大学生活动中心集合，请于本周五18:00前填写报名表。
                报名链接：https://example.edu.cn/volunteer
                #志愿 #报名
                """, MemoStatus.Inbox),
            Parse("""
                请大家查看群文件中的课程安排，如有疑问联系学习委员。
                #教学
                """, MemoStatus.Done),
            Parse($"""
                【就业宣讲】某科技公司校园招聘
                下周二下午3:00在就业指导中心举行宣讲会，现场接收简历。
                详情：https://example.edu.cn/career
                #就业 #招聘
                """, MemoStatus.Inbox)
        };

        foreach (var memo in notices)
        {
            if (memo.Status == MemoStatus.Done)
            {
                memo.CompletedAt = now.AddHours(-2);
            }

            store.Upsert(memo);
        }
    }

    private static Memo Parse(string text, MemoStatus status)
    {
        var memo = NoticeParser.Parse(text, "学院通知群").ToMemo();
        memo.Status = status;
        return memo;
    }
}
