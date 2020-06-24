# frozen_string_literal: true

# Checks all users that aren't local that the SSO data is still valid (if not they are
# suspended)
class CheckAllSsoUsers < ApplicationJob
  queue_as :default

  after_perform do |_job|
    # Queue again
    CheckAllSsoUsers.set(wait: 24.hour).perform_later
  end

  def perform
    User.where(local: false).find_in_batches { |group|
      group.each { |user|
        CheckSsoUserSuspensionJob.perform_later user.email
      }
    }
  end
end
