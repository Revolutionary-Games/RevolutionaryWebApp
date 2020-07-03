# frozen_string_literal: true

# General helpers for patreon group operations
module PatreonGroupHelper
  COMMUNITY_DEVBUILD_GROUP = 'Supporter'
  COMMUNITY_VIP_GROUP = 'VIP_supporter'

  # Returns true if group changes are needed
  def self.handle_patreon_pledge_obj(pledge, user)
    changes = false
    pledge_cents = pledge['attributes']['amount_cents']

    # Handle declined payments
    declined = !pledge['attributes']['declined_since'].nil?

    email = user['attributes']['email']
    patron = Patron.find_by email: email

    username = user['attributes']['vanity'] ||
               user['attributes']['full_name']

    if patron.nil? && !declined
      puts "We have a new patron #{username}"
      Patron.create!(suspended: false, username: username, email: email,
                     pledge_amount_cents: pledge_cents, marked: true)
      changes = true
    elsif declined
      if patron.suspended != true
        puts 'A patron is now in declined state. Setting as suspended'
        patron.suspended = true
        patron.suspended_reason = "Payment failed on Patreon"
        # These wait as the user will get saved in a bit
        CheckSsoUserSuspensionJob.set(wait: 30.seconds).perform_later patron.email
        changes = true
      end
      patron.marked = true
      patron.save!
    elsif patron.pledge_amount_cents != pledge_cents || patron.username != username
      puts 'A patron has changed their pledge_cents amount (or name)'

      patron.pledge_amount_cents = pledge_cents
      patron.username = username
      patron.marked = true
      patron.suspended = false
      patron.save!
      CheckSsoUserSuspensionJob.set(wait: 30.seconds).perform_later patron.email
      changes = true
    else
      patron.marked = true
      if patron.suspended
        patron.suspended = false
        CheckSsoUserSuspensionJob.set(wait: 30.seconds).perform_later patron.email
        changes = true
      end
      patron.save!
    end

    changes
  end

  def self.devbuild_group_members
    DiscourseApiHelper.query_users_in_group COMMUNITY_DEVBUILD_GROUP
  end

  def self.vip_group_members
    DiscourseApiHelper.query_users_in_group COMMUNITY_VIP_GROUP
  end

  # names of group owners
  def self.devbuild_group_owners
    DiscourseApiHelper.query_group_owners(COMMUNITY_DEVBUILD_GROUP).map { |i| i['username'] }
  end

  # names of group owners
  def self.vip_group_owners
    DiscourseApiHelper.query_group_owners(COMMUNITY_VIP_GROUP).map { |i| i['username'] }
  end

  def self.apply_adds_and_removes(devbuild_add, devbuild_remove, vip_add, vip_remove)
    unless devbuild_add.empty?
      DiscourseApiHelper.add_group_members COMMUNITY_DEVBUILD_GROUP, devbuild_add
    end

    unless devbuild_remove.empty?
      DiscourseApiHelper.remove_group_members COMMUNITY_DEVBUILD_GROUP, devbuild_remove
    end

    DiscourseApiHelper.add_group_members COMMUNITY_VIP_GROUP, vip_add unless vip_add.empty?

    return if vip_remove.empty?

    DiscourseApiHelper.remove_group_members COMMUNITY_VIP_GROUP, vip_remove
  end

  def self.should_be_group_for_patron(patron, patreon_settings)
    if !patron || !patreon_settings || patron.suspended
      :none
    elsif patron.pledge_amount_cents >= patreon_settings.vip_pledge_cents
      :vip
    elsif patron.pledge_amount_cents >= patreon_settings.devbuilds_pledge_cents
      :devbuild
    end
  end

  def self.forum_user_relevant_groups(user)
    result = []
    user['groups'].each { |group|
      if group['name'] == COMMUNITY_DEVBUILD_GROUP
        result.append :devbuild
      elsif group['name'] == COMMUNITY_VIP_GROUP
        result.append :vip
      end
    }

    result
  end
end
