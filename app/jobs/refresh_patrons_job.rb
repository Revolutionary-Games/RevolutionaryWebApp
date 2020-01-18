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
        email = data[:user]['attributes']['email']
        patron = Patron.find_by email: email

        username = data[:user]['attributes']['vanity'] ||
                   data[:user]['attributes']['full_name']
        pledge = data[:pledge]['attributes']['amount_cents']

        # TODO: does this need to handle data[:pledge]["attributes"]["declined_since"] ?
        # or does patreon eventually remove that patron?

        if patron.nil?
          puts "We have a new patron #{username}"
          Patron.create!(suspended: false, username: username, email: email,
                         pledge_amount_cents: pledge, marked: true)
          changes = true
        elsif patron.pledge_amount_cents != pledge || patron.username != username
          puts 'A patron has changed their pledge amount (or name)'

          patron.pledge_amount_cents = pledge
          patron.username = username
          patron.marked = true
          patron.save!
          changes = true
        else
          patron.marked = true
          patron.save!
        end
      }

      settings.last_refreshed = Time.now
      settings.save
    }

    Patron.where(marked: false).each { |patron|
      puts "Destroying patron (#{patron.id}) because it is unmarked"
      patron.destroy
    }

    if changes
      ApplyPatronForumGroups.perform_later
    elsif RefreshPatronsJob.apply_forum_groups_not_queued
      ApplyPatronForumGroups.set(wait: 5.minutes).perform_later
    end
  end
end
