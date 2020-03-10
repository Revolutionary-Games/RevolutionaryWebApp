# frozen_string_literal: true

# Makes sure forum groups are up to date based on the Patron data
class ApplyPatronForumGroups < ApplicationJob
  queue_as :default

  def self.check_unmarked(list, owners, to_remove_list)
    list.each { |existing|
      next if existing['marked_used']

      next unless owners.find { |user| user['id'] == existing['id'] }.nil?

      # Needs to be removed
      to_remove_list.push existing['username']
    }
  end

  def handle_patron(patron, corresponding_forum_user)
    corresponding_username = corresponding_forum_user['username']

    puts "Handling (#{patron.username}) #{corresponding_username}"

    should_be_group = PatreonGroupHelper.should_be_group_for_patron patron, @patreon_settings

    puts "Target group: #{should_be_group}"

    # Find and mark the entries as used
    exists_in_devbuild = false
    exists_in_vip = false

    @devbuild_existing.each { |user|
      next unless user['username'] == corresponding_username

      user['marked_used'] = true
      exists_in_devbuild = true
      break
    }
    @vip_existing.each { |user|
      next unless user['username'] == corresponding_username

      user['marked_used'] = true
      exists_in_vip = true
      break
    }

    is_devbuild_owner = !@devbuild_owners.find { |user|
      user['username'] == corresponding_username
    }.nil?
    is_vip_owner = !@vip_owners.find { |user| user['username'] == corresponding_username }.nil?

    # Remove from groups
    if should_be_group != :vip && exists_in_vip && !is_vip_owner
      # remove from VIP
      @users_to_remove_from_vip.push corresponding_username
    end

    if should_be_group != :devbuild && exists_in_devbuild && !is_devbuild_owner
      # remove from devbuilds
      @users_to_remove_from_devbuilds.push corresponding_username
    end

    # Add to groups
    if should_be_group == :vip && !exists_in_vip
      # add to VIP
      @users_to_add_to_vip.push corresponding_username
    end

    if should_be_group == :devbuild && !exists_in_devbuild
      # add to devbuilds
      @users_to_add_to_devbuilds.push corresponding_username
    end
  end

  def check_status(patron, status)
    return if patron.has_forum_account == status

    patron.has_forum_account = status
    patron.save!
  end

  def go_through_patrons
    # Doesn't exactly work great with multiple settings
    @patreon_settings = PatreonSettings.first

    Patron.all.each { |patron|
      # Skip patrons who shouldn't have a forum group, check_unmarked fill find them
      next if patron.pledge_amount_cents < @patreon_settings.devbuilds_pledge_cents

      # Also skip suspended who should have their groups revoked as long as they are suspended
      next if patron.suspended

      corresponding_forum_user = CommunityForumGroups.find_user_by_email patron.alias_or_email

      if !corresponding_forum_user
        puts "Patron (#{patron.username}) is missing a forum account, skipping applying groups"
        check_status patron, false
        next
      else
        check_status patron, true
      end

      handle_patron patron, corresponding_forum_user
    }
  end

  def apply_adds_and_removes
    PatreonGroupHelper.apply_adds_and_removes @users_to_add_to_devbuilds,
                                              @users_to_remove_from_devbuilds,
                                              @users_to_add_to_vip,
                                              @users_to_remove_from_vip
  end

  def perform
    unless ENV['COMMUNITY_DISCOURSE_API_KEY']
      raise 'COMMUNITY_DISCOURSE_API_KEY env variable is missing'
    end

    @devbuild_existing, @devbuild_owners = PatreonGroupHelper.devbuild_group_members
    @vip_existing, @vip_owners = PatreonGroupHelper.vip_group_members

    @users_to_add_to_devbuilds = []
    @users_to_remove_from_devbuilds = []

    @users_to_add_to_vip = []
    @users_to_remove_from_vip = []

    go_through_patrons

    # Remove people from the groups that aren't patrons (and aren't group owners)
    puts 'checking extraneous group members'

    ApplyPatronForumGroups.check_unmarked @devbuild_existing, @devbuild_owners,
                                          @users_to_remove_from_devbuilds
    ApplyPatronForumGroups.check_unmarked @vip_existing, @vip_owners, @users_to_remove_from_vip

    puts 'devbuild add: ', @users_to_add_to_devbuilds, 'devbuild remove:',
         @users_to_remove_from_devbuilds, 'vip add:', @users_to_add_to_vip,
         'vip remove:', @users_to_remove_from_vip

    apply_adds_and_removes
  end
end
