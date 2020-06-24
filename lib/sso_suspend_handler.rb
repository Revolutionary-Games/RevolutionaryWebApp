module SsoSuspendHandler
  def self.check_user(user)
    return if user.local

    should_be_suspended = true
    reason = 'SSO user no longer valid for login'

    case user.sso_source
    when 'patreon'
      patron = Patron.find_by email_alias: email

      patron ||= Patron.find_by email: email

      if !patron
        reason = 'No longer a patron'
      elsif patron.suspended
        reason = patron.suspended_reason
      else
        should_be_suspended = false
      end
    when 'communityforum'
      unless ENV['COMMUNITY_DISCOURSE_API_KEY']
        raise 'COMMUNITY_DISCOURSE_API_KEY env variable is missing'
      end

      discourse_user = DiscourseApiHelper.find_user_by_email email

      if discourse_user
        discourse_user = DiscourseApiHelper.user_info_by_name discourse_user['username']

        raise 'Second retrieve by name from community discourse failed' unless discourse_user

        found = false

        discourse_user['groups'].each { |group|
          if group['name'] == COMMUNITY_DEVBUILD_GROUP || group['name'] == COMMUNITY_VIP_GROUP
            found = true
            break
          end
        }

        if found
          should_be_suspended = false
        else
          reason = 'No longer part of required forum group'
        end
      end

    when 'devforum'
      raise 'DEV_DISCOURSE_API_KEY env variable is missing' unless ENV['DEV_DISCOURSE_API_KEY']

      discourse_user = DiscourseApiHelper.find_user_by_email email, type: :dev

      should_be_suspended = false if discourse_user
    else
      raise "Unknown SSO source in user: #{user.sso_source}"
    end

    apply_changed_suspend_status(email, reason, user) if user.suspended != should_be_suspended
  end

  def self.apply_changed_suspend_status(email, reason, user)
    # Don't unsuspend if was manually suspended
    if user.suspended && !user.suspended_manually
      logger.info "Unsuspending '#{email}' from sso sources"
      user.suspended = false
      user.save!
    elsif !user.suspended
      logger.info "Suspending '#{email}' from sso sources"
      user.suspended = true
      user.suspended_reason = "Used login option is no longer valid (#{reason})"
      user.save!
    end
  end
end
