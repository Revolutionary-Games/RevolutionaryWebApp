# frozen_string_literal: true

module API
  module V1
    # Devbuild API
    class DevBuildAPI < Grape::API
      include API::V1::Defaults

      MAX_OFFER_OBJECTS_BATCH = 100

      helpers do
        def access_key
          code = headers['X-Access-Code']

          return nil if code.blank?

          key = AccessKey.find_by key_code: code, key_type: KEY_TYPE_DEVBUILDS

          error!({ error_code: 401, message: 'Invalid access key' }, 401) unless key

          key.update_last_used

          key
        end
      end

      resource :devbuild do
        desc 'Checks if the server wants the specified devbuild'
        params do
          requires :build_hash, type: String, desc: 'Devbuild hash'
          requires :build_platform, type: String, desc: 'Devbuild platform'
        end
        post 'offer_devbuild' do
          key = access_key
          anonymous = key.nil?

          if anonymous
            upload = !DevBuild.find_by(build_hash: permitted_params[:build_hash],
                                       platform: permitted_params[:build_platform])&.uploaded?
          else
            upload = !DevBuild.find_by(build_hash: permitted_params[:build_hash],
                                       platform: permitted_params[:build_platform],
                                       anonymous: false)&.uploaded?
          end

          status 200
          { upload: upload }
        end

        desc 'Checks if the server wants any of the specified dehydrated objects'
        params do
          requires :objects, type: Array, desc: 'Offered objects (with sha3 and size keys)'
        end
        post 'offer_objects' do
          if permitted_params[:objects].size > MAX_OFFER_OBJECTS_BATCH
            error!(
              {
                error_code: 400,
                message: "Too many objects, max batch size is: #{MAX_OFFER_OBJECTS_BATCH}"
              }, 400
            )
          end

          access_key

          missing_objects = []

          permitted_params[:objects].each { |obj|
            unless DehydratedObject.find_by(sha3: obj['sha3'])&.uploaded?
              missing_objects.append obj['sha3']
            end
          }

          status 200
          { upload: missing_objects }
        end

        desc 'Starts upload of a devbuild. The required objects need to be already uploaded'
        params do
          requires :build_hash, type: String, desc: 'Devbuild hash'
          requires :build_branch, type: String, desc: 'Devbuild branch'
          requires :build_platform, type: String, desc: 'Devbuild platform'
          requires :build_size, type: Integer, desc: 'Size of the build'
          requires :build_zip_hash, type: String, desc: 'Hash of the build zip'
          requires :required_objects, type: Array, desc:
            'List of the objects needed by this build (just hash)'
        end
        post 'upload_devbuild' do
          key = access_key
          anonymous = key.nil?

          # Check that all objects exist
          permitted_params[:required_objects].each { |sha3|
            next if DehydratedObject.find_by(sha3: sha3).uploaded?

            error!(
              {
                error_code: 400,
                message: "Specified dehydrated object is not uploaded: #{sha3}"
              }, 400
            )
          }

          if permitted_params[:build_hash].blank? || permitted_params[:build_platform].blank?
            error!(
              {
                error_code: 400,
                message: 'Invalid build information'
              }, 400
            )
          end

          existing = DevBuild.find_by(build_hash: permitted_params[:build_hash],
                                      platform: permitted_params[:build_platform])

          if anonymous
            if existing
              error!(
                { error_code: 403,
                  message: "Can't upload over an existing devbuild without an access key" },
                403
              )
            end
          else
            # Non-anonymous upload can overwrite an anonymous upload
            if existing&.anonymous
              Rails.logger.info "Anonymous devbuild (#{existing.build_hash}) upload is " \
                                'being overwritten'
              existing.anonymous = false
              existing.save!
            elsif existing&.uploaded?
              error!(
                {
                  message: "Can't upload a new version of an existing build"
                }, 200
              )
            end
          end

          folder = StorageItem.devbuild_builds_folder

          unless existing
            sanitized_platform = permitted_params[:build_platform].gsub '/', ''
            existing = DevBuild.create!(
              build_hash: permitted_params[:build_hash],
              platform: permitted_params[:build_platform],
              branch: permitted_params[:build_branch],
              anonymous: anonymous,
              important: !anonymous && permitted_params[:build_branch] == 'master',
              # Create storage file to store it
              storage_item: StorageItem.find_or_create_by!(
                name: "#{permitted_params[:build_hash]}_" \
                      "#{sanitized_platform}.7z", parent: folder,
                ftype: 0, special: true, read_access: ITEM_ACCESS_USER,
                write_access: ITEM_ACCESS_NOBODY
              )
            )

            CountRemoteFolderItems.perform_later(folder.id)
          end

          # Apply objects
          permitted_params[:required_objects].each { |sha3|
            dehydrated = DehydratedObject.find_by sha3: sha3

            begin
              existing.dehydrated_objects << dehydrated
            rescue ActiveRecord::RecordNotUnique
              # This happens when the object is already added
            end
          }

          Rails.logger.info "Upload of (#{existing.build_hash} on #{existing.platform}) " \
                            "starting from #{request.ip}"

          # Upload a version of it
          version = existing.storage_item.next_version
          expires = Time.now + RemoteStorageHelper.upload_expire_time
          file = version.create_storage_item(expires, permitted_params[:build_size])

          status 200
          { upload_url: RemoteStorageHelper.create_put_url(file.storage_path),
            verify_token: RemoteStorageHelper.create_put_token(
              file.storage_path,
              file.size, file.id, {
                dev_build_id: existing.id,
                build_zip_hash: permitted_params[:build_zip_hash]
              }
            ) }
        end

        desc 'Starts upload of the specified objects'
        params do
          requires :objects, type: Array, desc: 'Objects to upload (with sha3 and size keys)'
        end
        post 'upload_objects' do
          key = access_key

          if permitted_params[:objects].size > MAX_OFFER_OBJECTS_BATCH
            error!(
              {
                error_code: 400,
                message: "Too many objects, max batch size is: #{MAX_OFFER_OBJECTS_BATCH}"
              }, 400
            )
          end

          uploads = []

          folder = StorageItem.dehydrated_folder

          expires = Time.now + RemoteStorageHelper.upload_expire_time

          new_items = false

          permitted_params[:objects].each { |obj|
            sha3 = obj['sha3']
            size = obj['size']
            dehydrated = DehydratedObject.find_by sha3: sha3

            if dehydrated&.uploaded?
              # Already uploaded
            else
              # Can upload this one
              unless dehydrated
                dehydrated = DehydratedObject.create! sha3: sha3, storage_item:

                  StorageItem.create!(
                    name: "#{sha3}.gz", parent: folder,
                    ftype: 0, special: true, read_access: ITEM_ACCESS_USER,
                    write_access: ITEM_ACCESS_NOBODY
                  )
                new_items = true
              end

              version = dehydrated.storage_item.next_version
              file = version.create_storage_item(expires, size)

              Rails.logger.info "Upload of dehydrated #{sha3} " \
                                "starting from #{request.ip}"

              uploads.append(
                {
                  sha3: sha3,
                  upload_url: RemoteStorageHelper.create_put_url(file.storage_path),
                  verify_token: RemoteStorageHelper.create_put_token(file.storage_path,
                                                                     file.size, file.id,
                                                                     { sha3: sha3 })
                }
              )
            end
          }

          CountRemoteFolderItems.perform_later(folder.id) if new_items

          status 200
          { upload: uploads }
        end

        desc 'Report finished devbuild / object upload'
        params do
          requires :token, type: String, desc: "Finished upload's token"
        end
        post 'finish' do
          key = access_key
          anonymous = key.nil?

          begin
            data, item, version = RemoteStorageHelper.handle_finished_token(
              permitted_params[:token]
            )
          rescue StandardError => e
            error!({ error_code: 400, message: e.to_s }, 400)
            return
          end

          # Check hash
          if data['custom'].include? 'sha3'
            needed_hash = data['custom']['sha3']

            hash = RemoteStorageHelper.calculate_ungzipped_hash item.storage_path

            if needed_hash != hash
              Rails.logger.warning 'Upload failed or someone tried to upload a the wrong ' \
                                   "file, due to hash mismatch (required) #{needed_hash} " \
                                   "!= #{hash} (from S3)"
              error!({ error_code: 400, message: 'Uploaded object hash is unexpected' }, 400)
            end

          elsif data['custom'].include? 'dev_build_id'

            build = DevBuild.find data['custom']['dev_build_id']

            if anonymous && !build.anonymous
              error!(
                { error_code: 403,
                  message: "Can't upload over a non-anonymous build without a key" },
                403
              )
            end

            # Update build hash if this is the latest version
            if build.storage_item.highest_version == version
              build.build_zip_hash = data['custom']['build_zip_hash']
              build.save!
            else
              Rails.logger.warn "Uploaded a non-highest version item, not updating hash"
            end
          end

          Rails.logger.info "DevBuild item (#{item.storage_path}) is now uploaded"

          item.uploading = false
          version.uploading = false
          version.storage_item.size = item.size
          item.save!
          version.save!
          version.storage_item.save!

          status 200
        end
      end
    end
  end
end
