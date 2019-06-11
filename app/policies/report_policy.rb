class ReportPolicy

  regulate_broadcast do |policy|

    policy.send_all.to(AdminUser)
    
    if public
      policy.send_all.to(Hyperstack::Application)
    else
      policy.send_all_but(:reporter_email).to(DeveloperUser)
      policy.send_only(:public).to(Hyperstack::Application)
    end
  end

end
