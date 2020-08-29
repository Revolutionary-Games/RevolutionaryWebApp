# frozen_string_literal: true

# Refreshes all patron info and applies groups
class RefreshPatronsJob < ApplicationJob
  queue_as :default

  def self.apply_forum_groups_not_queued
    Sidekiq::ScheduledSet.new.none? { |job|
      job.display_class == 'ApplyPatronForumGroups'
    }
  end

  after_perform do |_job|
    # Queue again
    RefreshPatronsJob.set(wait: 1.hour).perform_later
  end

  def perform
    # Mark all patrons as no longer valid
    Patron.update_all marked: false

    changes = false

    PatreonSettings.all.each { |settings|
      next unless settings.active

      patrons = settings.all_patrons

      patrons.each { |data|
        reward_id = nil

        if data[:reward]
          reward_id = data[:reward]["id"]
        end

        if PatreonGroupHelper.handle_patreon_pledge_obj data[:pledge], data[:user], reward_id
          changes = true
        end
      }

      settings.last_refreshed = Time.now
      settings.save
    }

    Patron.where(marked: false).each { |patron|
      logger.info "Destroying patron (#{patron.id}) because it is unmarked"
      patron.destroy
    }

    if changes
      ApplyPatronForumGroups.perform_later
    elsif RefreshPatronsJob.apply_forum_groups_not_queued
      ApplyPatronForumGroups.set(wait: 5.minutes).perform_later
    end
  end
end
