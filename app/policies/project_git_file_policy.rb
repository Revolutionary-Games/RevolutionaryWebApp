# frozen_string_literal: true

class ProjectGitFilePolicy
  regulate_broadcast do |policy|
    policy.send_all.to(DeveloperUser)

    if lfs_project.public
      policy.send_all.to(
        Hyperstack::Application
      )
    end
  end
end
