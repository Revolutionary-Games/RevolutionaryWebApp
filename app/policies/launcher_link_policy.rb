class LauncherLinkPolicy
  regulate_broadcast {|policy|
    # Users get their own links
    policy.send_all_but(:link_code).to(user)
  }
end
