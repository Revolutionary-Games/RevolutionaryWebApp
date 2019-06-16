class ReportPolicy

  regulate_broadcast do |policy|

    policy.send_all.to(AdminUser)
    policy.send_all_but(:reporter_email).to(DeveloperUser)
    
    if public
      policy.send_all_but(:reporter_email, :reporter_ip, :log_files).to(
        Hyperstack::Application)
    else
      policy.send_only(:public).to(Hyperstack::Application)
    end
  end

end
