from scripts.check_file_sizes import assess_file, validate_policy


def policy(reviews=None):
    return {
        "target_lines": 400,
        "strong_review_lines": 600,
        "critical_lines": 1000,
        "reviews": reviews or {},
    }


def test_files_within_target_need_no_review():
    assert assess_file("app/small.py", 400, policy()) == ([], [])


def test_file_above_target_requires_review_but_can_be_kept():
    warnings, failures = assess_file("app/large.py", 450, policy())
    assert warnings
    assert failures == ["app/large.py: 超过目标线但未登记职责与拆分评估"]

    reviewed = policy({
        "app/large.py": {
            "max_lines": 500,
            "decision": "keep",
            "reason": "单一状态机放在同一文件更容易追踪",
        }
    })
    assert assess_file("app/large.py", 450, reviewed)[1] == []


def test_reviewed_growth_and_split_follow_up_are_enforced():
    reviewed = policy({
        "app/large.py": {
            "max_lines": 620,
            "decision": "split",
            "reason": "职责已经混杂",
        }
    })
    _, failures = assess_file("app/large.py", 650, reviewed)
    assert "app/large.py: 650 行超过已评审上限 620 行" in failures
    assert "app/large.py: split 决策必须提供可追溯的 follow_up" in failures


def test_critical_handwritten_file_cannot_use_plain_keep():
    reviewed = policy({
        "app/huge.py": {
            "max_lines": 1200,
            "decision": "keep",
            "reason": "暂时保留",
        }
    })
    _, failures = assess_file("app/huge.py", 1100, reviewed)
    assert "app/huge.py: 超过 1000 行的手写文件不能使用普通 keep 决策" in failures


def test_policy_thresholds_must_be_strictly_increasing():
    invalid = policy()
    invalid["strong_review_lines"] = 400
    assert validate_policy(invalid)
